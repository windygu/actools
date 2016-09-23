﻿// #define LOCALIZABLE

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
#if LOCALIZABLE
using System.Globalization;
#endif
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using System.Threading;
using System.Windows;
using JetBrains.Annotations;

namespace AcManager {
    [Localizable(false)]
    internal class PackedHelper {
        public static bool OptionCache = true;
        public static bool OptionDirectLoading = false;

        private readonly string _logFilename;
        private readonly string _temporaryDirectory;
        private readonly ResourceManager _references;

        private List<string> _temporaryFiles;

        private static string Time() {
            var t = DateTime.Now;
            return $"{t.Hour:D2}:{t.Minute:D2}:{t.Second:D2}.{t.Millisecond:D3}";
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        private void Log(string s) {
            if (_logFilename == null) return;
            
            if (s == null) {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilename) ?? "");
                File.WriteAllBytes(_logFilename, new byte[0]);
            } else {
                using (var writer = new StreamWriter(_logFilename, true)) {
                    writer.WriteLine($"{Time()}: {s}");
                }
            }
        }

        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.ControlAppDomain)]
        public void SetUnhandledExceptionHandler() {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
        }

        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e) {
            Log("Unhandled exception: " + e.ExceptionObject);
            Environment.Exit(1);
        }

        internal ResolveEventHandler Handler { get; }

        internal PackedHelper(string appId, string referencesId, string logFilename) {
            _logFilename = logFilename;
            _temporaryDirectory = Path.Combine(Path.GetTempPath(), appId + "_libs");
            Directory.CreateDirectory(_temporaryDirectory);

            Log(null);
            Handler = HandlerImpl;

            _references = new ResourceManager(referencesId, Assembly.GetExecutingAssembly());

            if (logFilename != null) {
                SetUnhandledExceptionHandler();
            }
        }

        private static int Decompress(byte[] input, int inputOffset, int inputLength, byte[] output, int outputLength) {
            var iidx = (uint)inputOffset;
            uint oidx = 0;
            do {
                uint ctrl = input[iidx++];
                if (ctrl < 1 << 5) {
                    ctrl++;
                    do {
                        output[oidx++] = input[iidx++];
                    } while (--ctrl != 0);
                } else {
                    var len = ctrl >> 5;
                    var reference = (int)(oidx - ((ctrl & 0x1f) << 8) - 1);
                    if (len == 7) len += input[iidx++];
                    reference -= input[iidx++];
                    output[oidx++] = output[reference++];
                    output[oidx++] = output[reference++];
                    do {
                        output[oidx++] = output[reference++];
                    } while (--len != 0);
                }
            } while (iidx < inputLength);
            return (int)oidx;
        }

        private static byte[] DecompressSmart(byte[] input) {
            if (input.Length == 0) return new byte[0];
            var size = BitConverter.ToInt32(input, 0);
            var result = new byte[size];
            var decompress = Decompress(input, 4, input.Length - 4, result, result.Length);
            if (decompress != size) {
                throw new Exception($"Invalid data ({decompress}≠{size})");
            }
            return result;
        }

        [CanBeNull]
        private byte[] GetData(string id) {
            var bytes = _references.GetObject(id) as byte[];
            if (bytes == null) throw new Exception("Data is missing");

            if (_references.GetObject(id + "//fast") as bool? == true) {
                bytes = DecompressSmart(bytes);
            } else if (_references.GetObject(id + "//compressed") as bool? == true) {
                using (var memory = new MemoryStream(bytes))
                using (var output = new MemoryStream(bytes.Length * 2)) {
                    using (var decomp = new DeflateStream(memory, CompressionMode.Decompress)) {
                        decomp.CopyTo(output);
                    }

                    bytes = output.ToArray();
                }
            }

            return bytes;
        }

        private Assembly Extract(string id) {
            if (OptionDirectLoading && _references.GetObject(id + "//direct") as bool? == true) {
                var sw = Stopwatch.StartNew();
                var data = GetData(id);
                var unpacking = sw.ElapsedMilliseconds;
                sw.Restart();
                var assembly = Assembly.Load(data);

                if (_logFilename != null) {
                    Log("Direct: " + id + ", unpacking=" + unpacking + " ms, loading=" + sw.ElapsedMilliseconds + " ms");
                }

                return assembly;
            }

            Assembly result = null;
            string filename;
            try {
                filename = ExtractToFile(id);
            } catch (Exception e) {
                Log("Error: " + e);
                return null;
            }

            int i;
            for (i = 1; i < 20; i++) {
                try {
                    result = Assembly.LoadFrom(filename);
                    break;
                } catch (FileLoadException) {
                    Log("FileLoadException! Next attempt in 500 ms");
                    Thread.Sleep(500);
                }
            }

            if (result == null) throw new Exception("Can’t access unpacked library");
            if (i > 1) {
                Log($"{i + 1} attempt is successfull");
            }

            return result;
        }

        [NotNull]
        private string ExtractToFile(string id) {
            var hash = _references.GetString(id + "//hash");
            if (hash == null) throw new Exception($"Checksum for {id} is missing");

#if LOCALIZABLE
            _first = false;
#endif

            var prefix = id + "_";
            var name = prefix + hash + ".dll";
            var filename = Path.Combine(_temporaryDirectory, name);
            if (File.Exists(filename)) {
                if (_logFilename != null) {
                    Log("Already extracted: " + filename);
                }

                return filename;
            }

            Log("Extracting resource: " + filename);

            var bytes = GetData(id);
            if (bytes == null) throw new Exception($"Data for {id} is missing");

            if (_temporaryFiles == null) {
                _temporaryFiles = Directory.GetFiles(_temporaryDirectory, "*.dll").Select(Path.GetFileName).ToList();
            }

            var previous = _temporaryFiles.FirstOrDefault(x => x.StartsWith(prefix));
            if (previous != null) {
                Log("Removing previous version: " + previous);
                try {
                    File.Delete(Path.Combine(_temporaryDirectory, previous));
                    _temporaryFiles.Remove(previous);
                } catch (Exception e) {
                    Log("Can’t remove: " + e);
                }
            }

            Log("Writing, " + bytes.Length + " bytes");
            File.WriteAllBytes(filename, bytes);
            return filename;
        }

        private bool _ignore;

#if LOCALIZABLE
        private bool _first = true;
#endif

        private readonly Dictionary<string, Assembly> _cached = new Dictionary<string, Assembly>();

        private Assembly HandlerImpl(object sender, ResolveEventArgs args) {
            if (_ignore) return null;

            var id = new AssemblyName(args.Name).Name;

            Assembly result;
            if (_cached.TryGetValue(id, out result)) return result;
            
            if (string.Equals(id, "System.Web", StringComparison.OrdinalIgnoreCase)) {
                if (MessageBox.Show("Looks like you don’t have .NET 4 installed. Would you like to install it?", "Error",
                        MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes) {
                    Process.Start("http://www.microsoft.com/en-us/download/details.aspx?id=17718");
                }

                Environment.Exit(10);
            }

#if LOCALIZABLE
            if (name == "Content Manager.resources" && _first) {
                Log(">> Content Manager.resources <<");
                return null;
            }

            if (name.EndsWith(".resources")) {
                var culture = splitted.ElementAtOrDefault(2)?.Split(new[] { "Culture=" }, StringSplitOptions.None).ElementAtOrDefault(1);
                Log("culture: " + culture);
                if (culture == "neutral") return null;

                var resourceId = CultureInfo.CurrentUICulture.IetfLanguageTag;
                if (!string.Equals(resourceId, "en-US", StringComparison.OrdinalIgnoreCase)) {
                    name = name.Replace(".resources", "." + resourceId);
                    Log("localized: " + name);
                } else {
                    Log("skip: " + args.Name);
                    return null;
                }
            }
#else
            if (id.StartsWith("PresentationFramework") || id.EndsWith(".resources")) return null;
#endif

            if (id == "Magick.NET-x86") return null;

            if (_logFilename != null) {
                Log("Resolve: " + args.Name + " as " + id);
            }

            try {
                _ignore = true;
                result = Extract(id);

                if (OptionCache) {
                    _cached[id] = result;
                }

                return result;
            } finally {
                _ignore = false;
            }
        }
    }
}