﻿using GSMA.MobileConnect.Exceptions;
using GSMA.MobileConnect.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace GSMA.MobileConnect.Authentication
{
    /// <summary>
    /// Utility methods for token validation
    /// </summary>
    public static class TokenValidation
    {
        /// <summary>
        /// Validates an id token against the mobile connect validation requirements, this includes validation of some claims and validation of the signature
        /// </summary>
        /// <param name="idToken">IDToken to validate</param>
        /// <param name="clientId">ClientId that is validated against the aud and azp claims</param>
        /// <param name="issuer">Issuer that is validated against the iss claim</param>
        /// <param name="nonce">Nonce that is validated against the nonce claim</param>
        /// <param name="maxAge">MaxAge that is used to validate the auth_time claim (if supplied)</param>
        /// <param name="keyset">Keyset retrieved from the jwks url, used to validate the token signature</param>
        /// <param name="version">Version of MobileConnect services supported by current provider</param>
        /// <returns>TokenValidationResult that sepcfies if the token is valid, or if not why it is not valid</returns>
        public static TokenValidationResult ValidateIdToken(string idToken, string clientId, string issuer, string nonce, int? maxAge, JWKeyset keyset, string version)
        {
            if (string.IsNullOrEmpty(idToken))
            {
                return TokenValidationResult.IdTokenMissing;
            }

            bool isR1Source = version == Discovery.SupportedVersions.R1Version;
            TokenValidationResult result = ValidateIdTokenClaims(idToken, clientId, issuer, nonce, maxAge);
            if (result != TokenValidationResult.Valid && !isR1Source)
            {
                return result;
            }
            else if(isR1Source)
            {
                return TokenValidationResult.IdTokenValidationSkipped;
            }

            result = ValidateIdTokenSignature(idToken, keyset);
            return result != TokenValidationResult.Valid && isR1Source ? TokenValidationResult.IdTokenValidationSkipped : result;
        }

        /// <summary>
        /// Validates an id token signature by signing the id token payload and comparing the result with the signature
        /// </summary>
        /// <param name="idToken">IDToken to validate</param>
        /// <param name="keyset">
        /// Keyset retrieved from the jwks url, used to validate the token signature. 
        /// If null the token will not be validated and <see cref="TokenValidationResult.JWKSError"/> will be returned
        /// </param>
        /// <returns>TokenValidationResult that specifies if the token signature is valid, or if not why it is not valid</returns>
        public static TokenValidationResult ValidateIdTokenSignature(string idToken, JWKeyset keyset)
        {
            if (keyset == null)
            {
                return TokenValidationResult.JWKSError;
            }

            JObject header = JObject.Parse(JsonWebToken.DecodePart(idToken, JWTPart.Header));
            string alg = (string)header["alg"];

            string keyid = (string)header["kid"];
            var key = keyset.GetMatching(x => x.KeyID == keyid && (string.IsNullOrEmpty(x.Algorithm) || x.Algorithm == alg)).FirstOrDefault();
            if (key == null)
            {
                return TokenValidationResult.NoMatchingKey;
            }

            int lastSplitIndex = idToken.LastIndexOf('.');
            if (lastSplitIndex < 0 || lastSplitIndex == idToken.Length - 1)
            {
                return TokenValidationResult.InvalidSignature;
            }

            var dataToSign = idToken.Substring(0, lastSplitIndex);
            var signature = idToken.Substring(lastSplitIndex + 1);

            bool isValid;
            try
            {
                isValid = key.Verify(dataToSign, signature, alg);
            }
            catch (MobileConnectUnsupportedJWKException)
            {
                return TokenValidationResult.UnsupportedAlgorithm;
            }
            catch (MobileConnectInvalidJWKException)
            {
                return TokenValidationResult.KeyMisformed;
            }

            return isValid ? TokenValidationResult.Valid : TokenValidationResult.InvalidSignature;
        }

        /// <summary>
        /// Validates an id tokens claims using validation requirements from the mobile connect and open id connect specification
        /// </summary>
        /// <param name="idToken">IDToken to validate</param>
        /// <param name="clientId">ClientId that is validated against the aud and azp claims</param>
        /// <param name="issuer">Issuer that is validated against the iss claim</param>
        /// <param name="nonce">Nonce that is validated against the nonce claim</param>
        /// <param name="maxAge">MaxAge that is used to validate the auth_time claim (if supplied)</param>
        /// <returns>TokenValidationResult that specifies if the token claims are valid, or if not why they are not valid</returns>
        public static TokenValidationResult ValidateIdTokenClaims(string idToken, string clientId, string issuer, string nonce, int? maxAge)
        {
            JObject claims = JObject.Parse(JsonWebToken.DecodePart(idToken, JWTPart.Claims));
            if (nonce != null && (string)claims["nonce"] != nonce)
            {
                return TokenValidationResult.InvalidNonce;
            }

            if (!DoesAudOrAzpClaimMatchClientId(claims, clientId))
            {
                return TokenValidationResult.InvalidAudAndAzp;
            }

            var now = DateTime.UtcNow;
            var exp = (int?)claims["exp"];
            if (!exp.HasValue || UnixTimestamp.ToUTCDateTime(exp.Value) < now)
            {
                return TokenValidationResult.IdTokenExpired;
            }

            var authTime = (int?)claims["auth_time"];
            if (maxAge.HasValue && (!authTime.HasValue
                || UnixTimestamp.ToUTCDateTime(authTime.Value).AddSeconds(maxAge.Value) < now))
            {
                return TokenValidationResult.MaxAgePassed;
            }

            if ((string)claims["iss"] != issuer)
            {
                return TokenValidationResult.InvalidIssuer;
            }

            return TokenValidationResult.Valid;
        }

        /// <summary>
        /// Validates the access token contained in the token response data
        /// </summary>
        /// <param name="tokenResponse">Response data containing the access token and accompanying parameters</param>
        /// <returns>TokenValidationResult that specifies if the access token is valid, or if not why it is not valid</returns>
        public static TokenValidationResult ValidateAccessToken(RequestTokenResponseData tokenResponse)
        {
            if (string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                return TokenValidationResult.AccessTokenMissing;
            }

            if (tokenResponse.Expiry != null && tokenResponse.Expiry <= DateTime.UtcNow)
            {
                return TokenValidationResult.AccessTokenExpired;
            }

            return TokenValidationResult.Valid;
        }

        private static bool DoesAudOrAzpClaimMatchClientId(JObject claims, string clientId)
        {
            var audArray = claims["aud"] as JArray;
            if (audArray != null && audArray.FirstOrDefault(x => (string)x == clientId) == null)
            {
                return false;
            }

            if (audArray == null && (string)claims["aud"] != clientId)
            {
                return false;
            }

            var azp = (string)claims["azp"];
            return string.IsNullOrEmpty(azp) || azp == clientId;
        }
    }
}
