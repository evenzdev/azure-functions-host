﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.DependencyInjection
{
    /// <summary>
    /// An implementation of an <see cref="IWebJobsStartupTypeDiscoverer"/> that locates startup types
    /// from extension registrations.
    /// </summary>
    public class ScriptStartupTypeDiscoverer : IWebJobsStartupTypeDiscoverer
    {
        private readonly string _rootScriptPath;
        private readonly ILogger _logger;

        private static string[] _builtinExtensionAssemblies = GetBuiltinExtensionAssemblies();

        public ScriptStartupTypeDiscoverer(string rootScriptPath)
            : this(rootScriptPath, NullLogger.Instance)
        {
        }

        public ScriptStartupTypeDiscoverer(string rootScriptPath, ILogger logger)
        {
            _rootScriptPath = rootScriptPath ?? throw new ArgumentNullException(nameof(rootScriptPath));
            _logger = logger;
        }

        private static string[] GetBuiltinExtensionAssemblies()
        {
            return new[]
            {
                typeof(WebJobs.Extensions.Http.HttpWebJobsStartup).Assembly.GetName().Name,
                typeof(WebJobs.Extensions.ExtensionsWebJobsStartup).Assembly.GetName().Name
            };
        }

        public Type[] GetStartupTypes()
        {
            IEnumerable<Type> startupTypes = GetExtensionsStartupTypes();

            return startupTypes
                .Distinct(new TypeNameEqualityComparer())
                .ToArray();
        }

        public IEnumerable<Type> GetExtensionsStartupTypes()
        {
            string binPath = Path.Combine(_rootScriptPath, "bin");
            string metadataFilePath = Path.Combine(binPath, ScriptConstants.ExtensionsMetadataFileName);

            // parse the extensions file to get declared startup extensions
            ExtensionReference[] extensionItems = ParseExtensions(metadataFilePath);

            var startupTypes = new List<Type>();

            foreach (var item in extensionItems)
            {
                string startupExtensionName = item.Name ?? item.TypeName;
                _logger.LogInformation($"Loading startup extension '{startupExtensionName}'");

                // load the Type for each startup extension into the function assembly load context
                Type extensionType = Type.GetType(item.TypeName,
                    assemblyName =>
                    {
                        if (_builtinExtensionAssemblies.Contains(assemblyName.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning($"The extension startup type '{item.TypeName}' belongs to a builtin extension");
                            return null;
                        }

                        string path = item.HintPath;
                        if (string.IsNullOrEmpty(path))
                        {
                            path = assemblyName.Name + ".dll";
                        }

                        var hintUri = new Uri(path, UriKind.RelativeOrAbsolute);
                        if (!hintUri.IsAbsoluteUri)
                        {
                            path = Path.Combine(binPath, path);
                        }

                        if (File.Exists(path))
                        {
                            return FunctionAssemblyLoadContext.Shared.LoadFromAssemblyPath(path, true);
                        }

                        return null;
                    },
                    (assembly, typeName, ignoreCase) =>
                    {
                        return assembly?.GetType(typeName, false, ignoreCase);
                    }, false, true);

                if (extensionType == null)
                {
                    _logger.LogWarning($"Unable to load startup extension '{startupExtensionName}' (Type: '{item.TypeName}'). The type does not exist. Please validate the type and assembly names.");
                    continue;
                }
                if (!typeof(IWebJobsStartup).IsAssignableFrom(extensionType))
                {
                    _logger.LogWarning($"Type '{item.TypeName}' is not a valid startup extension. The type does not implement {nameof(IWebJobsStartup)}.");
                    continue;
                }

                startupTypes.Add(extensionType);
            }

            return startupTypes;
        }

        private ExtensionReference[] ParseExtensions(string metadataFilePath)
        {
            if (!File.Exists(metadataFilePath))
            {
                return Array.Empty<ExtensionReference>();
            }

            try
            {
                var extensionMetadata = JObject.Parse(File.ReadAllText(metadataFilePath));

                var extensionItems = extensionMetadata["extensions"]?.ToObject<List<ExtensionReference>>();
                if (extensionItems == null)
                {
                    _logger.LogError($"Unable to parse extensions metadata file '{metadataFilePath}'. Missing 'extensions' property.");
                    return Array.Empty<ExtensionReference>();
                }

                return extensionItems.ToArray();
            }
            catch (JsonReaderException exc)
            {
                _logger.LogError(exc, $"Unable to parse extensions metadata file '{metadataFilePath}'");

                return Array.Empty<ExtensionReference>();
            }
        }

        private class TypeNameEqualityComparer : IEqualityComparer<Type>
        {
            public bool Equals(Type x, Type y)
            {
                if (x == y)
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return string.Equals(x.FullName, y.FullName, StringComparison.Ordinal);
            }

            public int GetHashCode(Type obj)
            {
                if (obj == null)
                {
                    return 0;
                }

                return obj.FullName.GetHashCode();
            }
        }
    }
}
