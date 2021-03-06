﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public class ScriptHostIdProvider : IHostIdProvider
    {
        private const string HostIdPath = ConfigurationSectionNames.JobHost + ":id";
        private readonly IConfiguration _config;
        private readonly IEnvironment _environment;
        private readonly ScriptJobHostOptions _options;

        public ScriptHostIdProvider(IConfiguration config, IEnvironment environment, IOptions<ScriptJobHostOptions> options)
        {
            _config = config;
            _environment = environment;
            _options = options.Value;
        }

        public Task<string> GetHostIdAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_config[HostIdPath] ?? GetDefaultHostId(_environment, _options));
        }

        internal static string GetDefaultHostId(IEnvironment environment, ScriptJobHostOptions scriptOptions)
        {
            // We're setting the default here on the newly created configuration
            // If the user has explicitly set the HostID via host.json, it will overwrite
            // what we set here
            string hostId = null;
            if (scriptOptions.IsSelfHost)
            {
                // When running locally, derive a stable host ID from machine name
                // and root path. We use a hash rather than the path itself to ensure
                // IDs differ (due to truncation) between folders that may share the same
                // root path prefix.
                // Note that such an ID won't work in distributed scenarios, so should
                // only be used for local/CLI scenarios.
                string sanitizedMachineName = Environment.MachineName
                    .Where(char.IsLetterOrDigit)
                    .Aggregate(new StringBuilder(), (b, c) => b.Append(c)).ToString();
                hostId = $"{sanitizedMachineName}-{Math.Abs(GetStableHash(scriptOptions.RootScriptPath))}";
            }
            else
            {
                string uniqueSlotName = environment?.GetAzureWebsiteUniqueSlotName();
                if (!string.IsNullOrEmpty(uniqueSlotName))
                {
                    // If running on Azure Web App, derive the host ID from unique site slot name
                    hostId = uniqueSlotName;
                }
            }

            if (!string.IsNullOrEmpty(hostId))
            {
                if (hostId.Length > ScriptConstants.MaximumHostIdLength)
                {
                    // Truncate to the max host name length if needed
                    hostId = hostId.Substring(0, ScriptConstants.MaximumHostIdLength);
                }
            }

            // Lowercase and trim any trailing '-' as they can cause problems with queue names
            return hostId?.ToLowerInvariant().TrimEnd('-');
        }

        /// <summary>
        /// Computes a stable non-cryptographic hash
        /// </summary>
        /// <param name="value">The string to use for computation</param>
        /// <returns>A stable, non-cryptographic, hash</returns>
        internal static int GetStableHash(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            unchecked
            {
                int hash = 23;
                foreach (char c in value)
                {
                    hash = (hash * 31) + c;
                }
                return hash;
            }
        }
    }
}
