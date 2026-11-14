using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PrismaMusic;

public partial class MainWindow
{
    private static string TranslatorDirectory => Path.Combine(DataDirectory, "translation");
    private static string ManagedPython => Path.Combine(TranslatorDirectory, "venv", "Scripts", "python.exe");
    private static readonly SemaphoreSlim TranslationSetupGate = new(1);

    private static string FindBasePython()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var python = hive.OpenSubKey(@"Software\Python\PythonCore\3.12\InstallPath");
            if (python?.GetValue("ExecutablePath") is string executable && File.Exists(executable)) return executable;
            if (python?.GetValue(null) is string folder)
            {
                string candidate = Path.Combine(folder, "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static async Task<string> InstallLocalTranslator(string basePython, string root, string language, Action<string> progress)
    {
        await TranslationSetupGate.WaitAsync();
        try
        {
            var venv = Path.Combine(root, "venv");
            var python = Path.Combine(venv, "Scripts", "python.exe");
            Directory.CreateDirectory(root);
            if (!File.Exists(python))
            {
                if (string.IsNullOrWhiteSpace(basePython) || !Path.IsPathFullyQualified(basePython) || !File.Exists(basePython))
                    throw new InvalidOperationException(LocalTextExtension.Translate("Instala Python 3.12 o selecciona su ejecutable."));
                progress("Preparando el entorno local…");
                await RunTranslatorSetup(basePython, root, "-m", "venv", venv);
            }
            progress("Instalando Argos Translate…");
            await RunTranslatorSetup(python, root, "-m", "pip", "install", "--disable-pip-version-check", "argostranslate==1.11.0");
            progress("Descargando el modelo de idioma…");
            await RunTranslatorSetup(python, root, "-c", """
                import sys
                from argostranslate import package

                source = sys.argv[1]
                if not any(p.from_code == source and p.to_code == "es" for p in package.get_installed_packages()):
                    package.update_package_index()
                    match = next((p for p in package.get_available_packages() if p.from_code == source and p.to_code == "es"), None)
                    if match is None:
                        raise RuntimeError("No direct Argos model for " + source + " → es.")
                    package.install_from_path(match.download())
                """, language);
            return python;
        }
        finally { TranslationSetupGate.Release(); }
    }

    private static async Task RunTranslatorSetup(string python, string root, params string[] arguments)
    {
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        ConfigureTranslatorEnvironment(start, root);
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Python");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var errors = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await output;
            string error = await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static void ConfigureTranslatorEnvironment(ProcessStartInfo start, string root)
    {
        start.Environment["XDG_DATA_HOME"] = Path.Combine(root, "data");
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(root, "cache");
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(root, "config");
        start.Environment["ARGOS_CHUNK_TYPE"] = "MINISBD";
        start.Environment.Remove("ARGOS_PACKAGES_DIR");
    }
}
