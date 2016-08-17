﻿using GSMA.MobileConnect.Authentication;
using GSMA.MobileConnect.Discovery;
using GSMA.MobileConnect.Identity;
using GSMA.MobileConnect.Utils;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GSMA.MobileConnect
{
    /// <summary>
    /// Convenience wrapper for <see cref="IDiscoveryService"/> and <see cref="IAuthenticationService"/> methods for use with ASP.NET
    /// </summary>
    /// <seealso cref="MobileConnectInterface"/>
    /// <seealso cref="MobileConnectStatus"/>
    /// <seealso cref="MobileConnectConfig"/>
    /// <seealso cref="Web.ResponseConverter"/>
    public class MobileConnectWebInterface
    {
        private readonly IDiscoveryService _discovery;
        private readonly IAuthenticationService _authentication;
        private readonly IIdentityService _identity;
        private readonly IJWKeysetService _jwks;
        private readonly MobileConnectConfig _config;
        private readonly bool _cacheWithSessionId;

        /// <summary>
        /// Initializes a new instance of the MobileConnectWebInterface class
        /// </summary>
        /// <param name="discovery">Instance of IDiscovery concrete implementation</param>
        /// <param name="authentication">Instance of IAuthentication concrete implementation</param>
        /// <param name="identity">Instance of IIdentityService concrete implementation</param>
        /// <param name="jwks">Instance of IJWKeysetService concrete implementation</param>
        /// <param name="config">Configuration options</param>
        public MobileConnectWebInterface(IDiscoveryService discovery, IAuthenticationService authentication, IIdentityService identity, IJWKeysetService jwks, MobileConnectConfig config)
        {
            this._discovery = discovery;
            this._authentication = authentication;
            this._identity = identity;
            this._jwks = jwks;
            this._config = config;
            this._cacheWithSessionId = config.CacheResponsesWithSessionId && discovery.Cache != null;
        }

        /// <summary>
        /// Attempt discovery using the supplied parameters. If msisdn, mcc and mnc are null the result will be operator selection, otherwise valid parameters will result in a StartAuthorization status
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="msisdn">MSISDN from user</param>
        /// <param name="mcc">Mobile Country Code</param>
        /// <param name="mnc">Mobile Network Code</param>
        /// <param name="shouldProxyCookies">If cookies from the original request should be sent onto the discovery service</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> AttemptDiscoveryAsync(HttpRequestMessage request, string msisdn, string mcc, string mnc, bool shouldProxyCookies, MobileConnectRequestOptions options)
        {
            options.ClientIP = string.IsNullOrEmpty(options.ClientIP) ? request.GetClientIp() : options.ClientIP;

            var cookies = shouldProxyCookies ? request.GetCookies() : null;

            return await CacheIfRequired(await MobileConnectInterfaceHelper.AttemptDiscovery(_discovery, msisdn, mcc, mnc, cookies, _config, options));
        }

        /// <summary>
        /// Attempt discovery using the values returned from the operator selection redirect
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="redirectedUrl">Uri redirected to by the completion of the operator selection UI</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> AttemptDiscoveryAfterOperatorSelectionAsync(HttpRequestMessage request, Uri redirectedUrl)
        {
            return await CacheIfRequired(await MobileConnectInterfaceHelper.AttemptDiscoveryAfterOperatorSelection(_discovery, redirectedUrl, _config));
        }

        /// <summary>
        /// Creates an authorization url with parameters to begin the authetication process
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="discoveryResponse">The response returned by the discovery process</param>
        /// <param name="encryptedMSISDN">Encrypted MSISDN/Subscriber Id returned from the Discovery process</param>
        /// <param name="state">Unique string to be used to prevent Cross Site Forgery Request attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="nonce">Unique string to be used to prevent replay attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public MobileConnectStatus StartAuthentication(HttpRequestMessage request, DiscoveryResponse discoveryResponse, string encryptedMSISDN, string state, string nonce, MobileConnectRequestOptions options)
        {
            state = string.IsNullOrEmpty(state) ? GenerateUniqueString() : state;
            nonce = string.IsNullOrEmpty(nonce) ? GenerateUniqueString() : nonce;

            return MobileConnectInterfaceHelper.StartAuthentication(_authentication, discoveryResponse, encryptedMSISDN, state, nonce, _config, options);
        }

        /// <summary>
        /// Creates an authorization url with parameters to begin the authetication process, the SDKSession id is used to fetch the discovery response
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="sdkSession">SDKSession id used to fetch the discovery response with additional parameters that are required to generate the url</param>
        /// <param name="encryptedMSISDN">Encrypted MSISDN/Subscriber Id returned from the Discovery process</param>
        /// <param name="state">Unique string to be used to prevent Cross Site Forgery Request attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="nonce">Unique string to be used to prevent replay attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> StartAuthentication(HttpRequestMessage request, string sdkSession, string encryptedMSISDN, string state, string nonce, MobileConnectRequestOptions options)
        {
            var discoveryResponse = await GetSessionFromCache(sdkSession);

            if (discoveryResponse == null)
            {
                return GetCacheError();
            }

            return StartAuthentication(request, discoveryResponse, encryptedMSISDN, state, nonce, options);
        }

        /// <summary>
        /// Performs headless authentication followed by request token if successful. Tokens will be validated before being returned.
        /// This may be a long running method as it waits for the authenticating user to respond using their authenticating device.
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="discoveryResponse">The response returned by the discovery process</param>
        /// <param name="encryptedMSISDN">Encrypted MSISDN/Subscriber Id returned from the Discovery process</param>
        /// <param name="state">Unique string to be used to prevent Cross Site Forgery Request attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="nonce">Unique string to be used to prevent replay attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="options">Optional parameters</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel long running requests</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> RequestHeadlessAuthenticationAsync(HttpRequestMessage request, DiscoveryResponse discoveryResponse, string encryptedMSISDN, string state, string nonce, 
            MobileConnectRequestOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            state = string.IsNullOrEmpty(state) ? GenerateUniqueString() : state;
            nonce = string.IsNullOrEmpty(nonce) ? GenerateUniqueString() : nonce;

            return await MobileConnectInterfaceHelper.RequestHeadlessAuthentication(_authentication, _jwks, _identity, discoveryResponse, encryptedMSISDN, state, nonce, _config, options, cancellationToken);
        }

        /// <summary>
        /// Performs headless authentication followed by request token if successful. Tokens will be validated before being returned.
        /// This may be a long running method as it waits for the authenticating user to respond using their authenticating device.
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="sdkSession">SDKSession id used to fetch the discovery response with additional parameters that are required to generate the url</param>
        /// <param name="encryptedMSISDN">Encrypted MSISDN/Subscriber Id returned from the Discovery process</param>
        /// <param name="state">Unique string to be used to prevent Cross Site Forgery Request attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="nonce">Unique string to be used to prevent replay attacks during request token process (defaults to guid if not supplied, value will be returned in MobileConnectStatus object)</param>
        /// <param name="options">Optional parameters</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel long running requests</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> RequestHeadlessAuthenticationAsync(HttpRequestMessage request, string sdkSession, string encryptedMSISDN, string state, string nonce, 
            MobileConnectRequestOptions options, CancellationToken cancellationToken = default(CancellationToken))
        {
            var discoveryResponse = await GetSessionFromCache(sdkSession);

            if (discoveryResponse == null)
            {
                return GetCacheError();
            }

            return await RequestHeadlessAuthenticationAsync(request, discoveryResponse, encryptedMSISDN, state, nonce, options, cancellationToken);
        }

        /// <summary>
        /// Request token using the values returned from the authorization redirect
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="discoveryResponse">The response returned by the discovery process</param>
        /// <param name="redirectedUrl">Uri redirected to by the completion of the authorization UI</param>
        /// <param name="expectedState">The state value returned from the StartAuthorization call should be passed here, it will be used to validate the authenticity of the authorization process</param>
        /// <param name="expectedNonce">The nonce value returned from the StartAuthorization call should be passed here, it will be used to ensure the token was not requested using a replay attack</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> RequestTokenAsync(HttpRequestMessage request, DiscoveryResponse discoveryResponse, Uri redirectedUrl, string expectedState, string expectedNonce, MobileConnectRequestOptions options)
        {
            return await MobileConnectInterfaceHelper.RequestToken(_authentication, _jwks, discoveryResponse, redirectedUrl, expectedState, expectedNonce, _config, options);
        }

        /// <summary>
        /// Request token using the values returned from the authorization redirect
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="sdkSession">SDKSession id used to fetch the discovery response with additional parameters that are required to request a token</param>
        /// <param name="redirectedUrl">Uri redirected to by the completion of the authorization UI</param>
        /// <param name="expectedState">The state value returned from the StartAuthorization call should be passed here, it will be used to validate the authenticity of the authorization process</param>
        /// <param name="expectedNonce">The nonce value returned from the StartAuthorization call should be passed here, it will be used to ensure the token was not requested using a replay attack</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> RequestTokenAsync(HttpRequestMessage request, string sdkSession, Uri redirectedUrl, string expectedState, string expectedNonce, MobileConnectRequestOptions options)
        {
            var discoveryResponse = await GetSessionFromCache(sdkSession);

            if (discoveryResponse == null)
            {
                return GetCacheError();
            }

            return await RequestTokenAsync(request, discoveryResponse, redirectedUrl, expectedState, expectedNonce, options);
        }

        /// <summary>
        /// Handles continuation of the process following a completed redirect, the request token url must be provided if it has been returned by the discovery process.
        /// Only the request and redirectedUrl are required, however if the redirect being handled is the result of calling the Authorization URL then the remaining parameters are required.
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="redirectedUrl">Url redirected to by the completion of the previous step</param>
        /// <param name="discoveryResponse">The response returned by the discovery process</param>
        /// <param name="expectedState">The state value returned from the StartAuthorization call should be passed here, it will be used to validate the authenticity of the authorization process</param>
        /// <param name="expectedNonce">The nonce value returned from the StartAuthorization call should be passed here, it will be used to ensure the token was not requested using a replay attack</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> HandleUrlRedirectAsync(HttpRequestMessage request, Uri redirectedUrl, DiscoveryResponse discoveryResponse = null, string expectedState = null, string expectedNonce = null, MobileConnectRequestOptions options = null)
        {
            return await CacheIfRequired(await MobileConnectInterfaceHelper.HandleUrlRedirect(_discovery, _authentication, _jwks, redirectedUrl, discoveryResponse, expectedState, expectedNonce, _config, options));
        }

        /// <summary>
        /// Handles continuation of the process following a completed redirect, the request token url must be provided if it has been returned by the discovery process.
        /// Only the request and redirectedUrl are required, however if the redirect being handled is the result of calling the Authorization URL then the remaining parameters are required.
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="redirectedUrl">Url redirected to by the completion of the previous step</param>
        /// <param name="sdkSession">SDKSession id used to fetch the discovery response with additional parameters that are required to request a token</param>
        /// <param name="expectedState">The state value returned from the StartAuthorization call should be passed here, it will be used to validate the authenticity of the authorization process</param>
        /// <param name="expectedNonce">The nonce value returned from the StartAuthorization call should be passed here, it will be used to ensure the token was not requested using a replay attack</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with required information for continuing the mobileconnect process</returns>
        public async Task<MobileConnectStatus> HandleUrlRedirectAsync(HttpRequestMessage request, Uri redirectedUrl, string sdkSession = null, string expectedState = null, string expectedNonce = null, MobileConnectRequestOptions options = null)
        {
            var discoveryResponse = await GetSessionFromCache(sdkSession);

            if (discoveryResponse == null && (expectedNonce != null || expectedState != null || sdkSession != null))
            {
                return GetCacheError();
            }

            return await CacheIfRequired(await MobileConnectInterfaceHelper.HandleUrlRedirect(_discovery, _authentication, _jwks, redirectedUrl, discoveryResponse, expectedState, expectedNonce, _config, options));
        }

        /// <summary>
        /// Request user info using the access token returned by <see cref="RequestTokenAsync(HttpRequestMessage, DiscoveryResponse, Uri, string, string, MobileConnectRequestOptions)"/>
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="discoveryResponse">The response returned by the discovery process</param>
        /// <param name="accessToken">Access token returned from RequestToken required to authenticate the request</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with requested UserInfo information</returns>
        public async Task<MobileConnectStatus> RequestUserInfoAsync(HttpRequestMessage request, DiscoveryResponse discoveryResponse, string accessToken, MobileConnectRequestOptions options)
        {
            return await MobileConnectInterfaceHelper.RequestUserInfo(_identity, discoveryResponse, accessToken, _config, options);
        }

        /// <summary>
        /// Request user info using the access token returned by <see cref="RequestTokenAsync(HttpRequestMessage, DiscoveryResponse, Uri, string, string, MobileConnectRequestOptions)"/>
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="sdkSession">SDKSession id used to fetch the discovery response with additional parameters that are required to request a user info</param>
        /// <param name="accessToken">Access token returned from RequestToken required to authenticate the request</param>
        /// <param name="options">Additional optional parameters</param>
        /// <returns>MobileConnectStatus object with requested UserInfo information</returns>
        public async Task<MobileConnectStatus> RequestUserInfoAsync(HttpRequestMessage request, string sdkSession, string accessToken, MobileConnectRequestOptions options)
        {
            var discoveryResponse = await GetSessionFromCache(sdkSession);

            if (discoveryResponse == null)
            {
                return GetCacheError();
            }

            return await RequestUserInfoAsync(request, discoveryResponse, accessToken, options);
        }

        /// <summary>
        /// Request identity using the access token returned by <see cref="RequestTokenAsync(HttpRequestMessage, DiscoveryResponse, Uri, string, string, MobileConnectRequestOptions)"/>
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="discoveryResponse">The response returned by the discovery process</param>
        /// <param name="accessToken">Access token returned from RequestToken required to authenticate the request</param>
        /// <param name="options">Optional parameters</param>
        /// <returns>MobileConnectStatus object with requested Identity information</returns>
        public async Task<MobileConnectStatus> RequestIdentityAsync(HttpRequestMessage request, DiscoveryResponse discoveryResponse, string accessToken, MobileConnectRequestOptions options)
        {
            return await MobileConnectInterfaceHelper.RequestIdentity(_identity, discoveryResponse, accessToken, _config, options);
        }

        /// <summary>
        /// Request identity using the access token returned by <see cref="RequestTokenAsync(HttpRequestMessage, DiscoveryResponse, Uri, string, string, MobileConnectRequestOptions)"/>
        /// </summary>
        /// <param name="request">Originating web request</param>
        /// <param name="sdkSession">SDKSession id used to fetch the discovery response with additional parameters that are required to request a user info</param>
        /// <param name="accessToken">Access token returned from RequestToken required to authenticate the request</param>
        /// <param name="options">Additional optional parameters</param>
        /// <returns>MobileConnectStatus object with requested Identity information</returns>
        public async Task<MobileConnectStatus> RequestIdentityAsync(HttpRequestMessage request, string sdkSession, string accessToken, MobileConnectRequestOptions options)
        {
            var discoveryResponse = await GetSessionFromCache(sdkSession);

            if (discoveryResponse == null)
            {
                return GetCacheError();
            }

            return await RequestIdentityAsync(request, discoveryResponse, accessToken, options);
        }

        private string GenerateUniqueString()
        {
            return Guid.NewGuid().ToString("N");
        }

        private async Task<MobileConnectStatus> CacheIfRequired(MobileConnectStatus status)
        {
            if (!_cacheWithSessionId || status.ResponseType != MobileConnectResponseType.StartAuthentication || status.DiscoveryResponse == null)
            {
                return status;
            }

            var sessionId = GenerateUniqueString();
            await _discovery.Cache.Add(sessionId, status.DiscoveryResponse).ConfigureAwait(false);
            status.SDKSession = sessionId;

            return status;
        }

        private async Task<DiscoveryResponse> GetSessionFromCache(string sessionId)
        {
            if (!_cacheWithSessionId || string.IsNullOrEmpty(sessionId))
            {
                return null;
            }

            return await _discovery.Cache.Get<DiscoveryResponse>(sessionId);
        }

        private MobileConnectStatus GetCacheError()
        {
            if (!_cacheWithSessionId)
            {
                return MobileConnectStatus.Error("cache_disabled", "cache is not enabled for session id caching of discovery responses", null);
            }

            return MobileConnectStatus.Error("sdksession_not_found", "session not found or expired, please try again", null);
        }
    }
}
