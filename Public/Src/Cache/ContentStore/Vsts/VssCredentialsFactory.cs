// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
#if !PLATFORM_OSX
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
#else
using BuildXL.Cache.ContentStore.Exceptions;
#endif

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     Factory for asynchronously creating VssCredentials
    /// </summary>
    public class VssCredentialsFactory
    {
        private readonly VssCredentials _credentials;

#if !PLATFORM_OSX
        private readonly SecureString _pat;
        private readonly byte[] _credentialBytes;

        private readonly VsoCredentialHelper _helper;

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        public VssCredentialsFactory(VsoCredentialHelper helper)
        {
            _helper = helper;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public VssCredentialsFactory(VsoCredentialHelper helper, VssCredentials credentials)
            : this(helper)
        {
            _credentials = credentials;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public VssCredentialsFactory(VsoCredentialHelper helper, SecureString pat)
            : this(helper)
        {
            _pat = pat;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public VssCredentialsFactory(VsoCredentialHelper helper, byte[] value)
            : this(helper)
        {
            _credentialBytes = value;
        }
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        public VssCredentialsFactory(VssCredentials creds)
        {
            _credentials = creds;
        }

        private const string VsoAadSettings_ProdAadAddress = "https://login.windows.net/";
        private const string VsoAadSettings_TestAadAddress = "https://login.windows-ppe.net/";
        private const string VsoAadSettings_DefaultTenant = "microsoft.com";

        public const string AadUserNameEnvVar = "VSTSAADUSERNAME";

        private Task<VssCredentials> CreateVssCredentialsForUserNameAsync(Uri baseUri, string userName)
        {
            var authorityAadAddres = baseUri.Host.ToLowerInvariant().Contains("visualstudio.com")
                ? VsoAadSettings_ProdAadAddress
                : VsoAadSettings_TestAadAddress;
            var authCtx = new AuthenticationContext(authorityAadAddres + VsoAadSettings_DefaultTenant);

            var userCred = userName == null
                ? new UserCredential() 
                : new UserCredential(userName);

            var token = new VssAadToken(authCtx, userCred, VssAadTokenOptions.None);
            token.AcquireToken(); 
            return Task.FromResult<VssCredentials>(new VssAadCredential(token));
        }

        /// <summary>
        /// Creates a VssCredentials object and returns it.
        /// </summary>
        public async Task<VssCredentials> CreateVssCredentialsAsync(Uri baseUri, bool useAad)
        {
            if (_credentials != null)
            {
                return _credentials;
            }

            if (_pat != null)
            {
                return _helper.GetPATCredentials(_pat);
            }

#if PLATFORM_OSX
            throw new CacheException("On non-Windows platforms only PAT-based VSTS authentication is allowed.");
#elif FEATURE_CORECLR
            return await CreateVssCredentialsForUserNameAsync(baseUri, Environment.GetEnvironmentVariable(AadUserNameEnvVar))
                .ConfigureAwait(false);
#else
            return await _helper.GetCredentialsAsync(baseUri, useAad, _credentialBytes, null)
                .ConfigureAwait(false);
#endif
        }
    }
}
