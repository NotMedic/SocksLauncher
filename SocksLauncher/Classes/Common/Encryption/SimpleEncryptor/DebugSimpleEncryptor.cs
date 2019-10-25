﻿using Common.Classes.Encryption;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

namespace Common.Encryption.SimpleEncryptor
{

    /// <summary>
    /// This is a simple encryptor 
    /// </summary>
    public class DebugSimpleEncryptor : IEncryptionHelper
    {
        List<byte> _key = new List<byte>();
        
        public DebugSimpleEncryptor(String base64Key)
        {
            byte[] key = Convert.FromBase64String(base64Key);
            System.Security.Cryptography.ProtectedMemory.Protect(key, System.Security.Cryptography.MemoryProtectionScope.SameProcess);
            _key.AddRange(key);
            Array.Clear(key, 0, key.Length);
        }

        public DebugSimpleEncryptor(SecureString base64Key)
        {
            IntPtr valuePtr = IntPtr.Zero;
            try
            {
                valuePtr = Marshal.SecureStringToGlobalAllocUnicode(base64Key);
                var key = Convert.FromBase64String(Marshal.PtrToStringUni(valuePtr));
                System.Security.Cryptography.ProtectedMemory.Protect(key, System.Security.Cryptography.MemoryProtectionScope.SameProcess);
                _key.AddRange(key);
                Array.Clear(key, 0, key.Length);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);
            }
        }
        
        public List<byte> Decrypt(string encodedEncPayload)
        {
            var ciphrBytes = Convert.FromBase64String(encodedEncPayload).ToList();
            using (var aes = new System.Security.Cryptography.RijndaelManaged())
            {
                aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                aes.IV = ciphrBytes.Take(16).ToArray();
                var key = _key.ToArray();
                System.Security.Cryptography.ProtectedMemory.Unprotect(key, System.Security.Cryptography.MemoryProtectionScope.SameProcess);
                aes.Key = key;
                var dec = aes.CreateDecryptor();
                var encBytes = ciphrBytes.Skip(16).ToArray();
                var plainbytes = dec.TransformFinalBlock(encBytes, 0, encBytes.Length).ToList();
                Array.Clear(key, 0, key.Length);
                return plainbytes;
            }   
        }

        public string Encrypt(List<byte> payload)
        {
            using (var aes = new System.Security.Cryptography.RijndaelManaged())
            {
                var result = new List<byte>();
                aes.Mode = System.Security.Cryptography.CipherMode.CBC;
                aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
                aes.GenerateIV();
                var key = _key.ToArray();
                System.Security.Cryptography.ProtectedMemory.Unprotect(key, System.Security.Cryptography.MemoryProtectionScope.SameProcess);
                aes.Key = key;
                var enc = aes.CreateEncryptor();
                result.AddRange(aes.IV);
                result.AddRange(enc.TransformFinalBlock(payload.ToArray(), 0, payload.Count));
                Array.Clear(key, 0, key.Length);
                return System.Convert.ToBase64String(result.ToArray());
            }
        }

        public string Initialize()
        {
            //NO WORK TO DO IN THIS VERSION
            return "USING DEBUG SIMPLE ENCRYPTOR";
        }
    }
}
