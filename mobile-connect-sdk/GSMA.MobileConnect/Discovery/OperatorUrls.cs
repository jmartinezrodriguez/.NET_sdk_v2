﻿using GSMA.MobileConnect.Constants;
using GSMA.MobileConnect.Json;
using System.Collections.Generic;
using System.Linq;

namespace GSMA.MobileConnect.Discovery
{
    /// <summary>
    /// Object to hold the operator specific urls returned from a successful discovery process call
    /// </summary>
    public class OperatorUrls
    {
        /// <summary>
        /// Url for authorization call
        /// </summary>
        public string AuthorizationUrl { get; set; }

        /// <summary>
        /// Url for token request call
        /// </summary>
        public string RequestTokenUrl { get; set; }

        /// <summary>
        /// Url for user info call
        /// </summary>
        public string UserInfoUrl { get; set; }

        /// <summary>
        /// Url for identity services call
        /// </summary>
        public string PremiumInfoUrl { get; set; }

        /// <summary>
        /// Url for JWKS info
        /// </summary>
        public string JWKSUrl { get; set; }

        /// <summary>
        /// Url for token refresh call
        /// </summary>
        public string RefreshTokenUrl { get; set; }

        /// <summary>
        /// Url for token revoke call
        /// </summary>
        public string RevokeTokenUrl { get; set; }

        /// <summary>
        /// Url for Provider Metadata
        /// </summary>
        public string ProviderMetadataUrl { get; set; }

        /// <summary>
        /// Url for Scope
        /// </summary>
        public string ScopeUrl { get; set; }

        /// <summary>
        /// Parses the operator urls from the parsed DiscoveryResponseData
        /// </summary>
        /// <param name="data">Data from the successful discovery response</param>
        /// <returns>Parsed operator urls or null if no urls found</returns>
        public static OperatorUrls Parse(DiscoveryResponseData data)
        {
            var links = data?.response?.apis?.operatorid?.link;
            if (links == null)
            {
                return null;
            }

            return new OperatorUrls()
            {
                AuthorizationUrl = GetUrl(links, LinkRels.AUTHORIZATION),
                RequestTokenUrl = GetUrl(links, LinkRels.TOKEN),
                UserInfoUrl = GetUrl(links, LinkRels.USERINFO),
                PremiumInfoUrl = GetUrl(links, LinkRels.PREMIUMINFO),
                JWKSUrl = GetUrl(links, LinkRels.JWKS),
                RefreshTokenUrl = GetUrl(links, LinkRels.TOKENREFRESH),
                RevokeTokenUrl = GetUrl(links, LinkRels.TOKENREVOKE),
                ProviderMetadataUrl = GetUrl(links, LinkRels.OPENID_CONFIGURATION),
                ScopeUrl = GetUrl(links, LinkRels.SCOPE)
            };
        }

        /// <summary>
        /// Replaces URLs from the discovery response with URLs from the provider metadata.
        /// This allows providers to use temporary urls while the main url is down for maintenance.
        /// </summary>
        /// <param name="metadata">Metadata to get overriding urls from</param>
        internal void OverrideUrls(ProviderMetadata metadata)
        {
            if(metadata == null)
            {
                return;
            }

            AuthorizationUrl = metadata.AuthorizationEndpoint ?? AuthorizationUrl;
            RequestTokenUrl = metadata.TokenEndpoint ?? RequestTokenUrl;
            UserInfoUrl = metadata.UserInfoEndpoint ?? UserInfoUrl;
            PremiumInfoUrl = metadata.PremiumInfoEndpoint ?? PremiumInfoUrl;
            JWKSUrl = metadata.JwksUri ?? JWKSUrl;
            RefreshTokenUrl = metadata.RefreshEndpoint ?? RefreshTokenUrl;
            RevokeTokenUrl = metadata.RevokeEndpoint ?? RevokeTokenUrl;
        }

        private static string GetUrl(IEnumerable<Link> links, string rel)
        {
            return links.FirstOrDefault(x => x.rel == rel)?.href;
        }

        /// <summary>
        /// Get list of operators urls
        /// </summary>
        /// <returns>List of operators urls</returns>
        public List<string> GetListOfUrls()
        {
            var list = new List<string>();
            list.Add(AuthorizationUrl);
            list.Add(RequestTokenUrl);
            list.Add(UserInfoUrl);
            list.Add(PremiumInfoUrl);
            list.Add(JWKSUrl);
            list.Add(RefreshTokenUrl);
            list.Add(RevokeTokenUrl);
            list.Add(ProviderMetadataUrl);
            list.Add(ScopeUrl);
            return list;
        }

        /// <summary>
        /// Get list of operators rels
        /// </summary>
        /// <returns>List of operators rels</returns>
        public List<string> GetListOfRels()
        {
            var list = new List<string>();
            list.Add(LinkRels.AUTHORIZATION);
            list.Add(LinkRels.TOKEN);
            list.Add(LinkRels.USERINFO);
            list.Add(LinkRels.PREMIUMINFO);
            list.Add(LinkRels.JWKS);
            list.Add(LinkRels.TOKENREFRESH);
            list.Add(LinkRels.TOKENREVOKE);
            list.Add(LinkRels.OPENID_CONFIGURATION);
            list.Add(LinkRels.SCOPE);
            return list;
        }
    }
}
