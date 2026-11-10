using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PrismaMusic;

public partial class MainWindow
{
    private static string TranslatorDirectory => Path.Combine(DataDirectory, "translation");
    private static string ManagedPython => Path.Combine(TranslatorDirectory, "python", "python.exe");
    private const string PythonEmbedUrl = "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip";
    private const string PythonEmbedSha256 = "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3";
    private const string GetPipUrl = "https://bootstrap.pypa.io/get-pip.py";
    private static readonly SemaphoreSlim TranslationSetupGate = new(1);
    private static readonly HttpClient TranslatorHttp = new() { Timeout = TimeSpan.FromMinutes(15) };

    private static async Task<string> InstallLocalTranslator(string basePython, string root, string language, Action<string> progress)
    {
        await TranslationSetupGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(root);
            string python;
            if (string.IsNullOrWhiteSpace(basePython))
            {
                python = await EnsureManagedPython(root, progress);
            }
            else if (string.Equals(Path.GetFullPath(basePython), Path.GetFullPath(ManagedPython), StringComparison.OrdinalIgnoreCase))
                python = await EnsureManagedPython(root, progress);
            else
            {
                if (!Path.IsPathFullyQualified(basePython) || !File.Exists(basePython))
                    throw new InvalidOperationException(LocalTextExtension.Translate("No se encontró el ejecutable de Python."));
                var venv = Path.Combine(root, "venv");
                python = Path.Combine(venv, "Scripts", "python.exe");
                if (!File.Exists(python))
                {
                    progress("Preparando el entorno local…");
                    await RunTranslatorSetup(basePython, root, "-m", "venv", venv);
                }
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

    private static async Task<string> EnsureManagedPython(string root, Action<string> progress)
    {
        string pythonDirectory = Path.Combine(root, "python");
        string python = Path.Combine(pythonDirectory, "python.exe");
        string configuration = Path.Combine(pythonDirectory, "python312._pth");
        string archiveLibrary = Path.Combine(pythonDirectory, "python312.zip");
        if (!File.Exists(python) || !File.Exists(configuration) || !File.Exists(archiveLibrary))
        {
            progress("Descargando el runtime integrado de Python…");
            string archive = Path.Combine(root, "python-embed.zip");
            string staging = Path.Combine(root, "python-staging");
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
                await DownloadTranslatorAsset(PythonEmbedUrl, archive, 32 * 1024 * 1024, PythonEmbedSha256);
                ZipFile.ExtractToDirectory(archive, staging, true);
                var stagedConfiguration = Path.Combine(staging, "python312._pth");
                ConfigureManagedPython(stagedConfiguration);
                if (Directory.Exists(pythonDirectory)) Directory.Delete(pythonDirectory, true);
                Directory.Move(staging, pythonDirectory);
            }
            finally
            {
                if (File.Exists(archive)) File.Delete(archive);
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        ConfigureManagedPython(configuration);
        string pipMarker = Path.Combine(pythonDirectory, "Lib", "site-packages", "pip", "__init__.py");
        if (!File.Exists(pipMarker))
        {
            progress("Preparando el gestor seguro de paquetes…");
            string getPip = Path.Combine(root, "get-pip.py");
            try
            {
                await DownloadTranslatorAsset(GetPipUrl, getPip, 8 * 1024 * 1024);
                await RunTranslatorSetup(python, root, "-X", "utf8", getPip, "--disable-pip-version-check");
            }
            finally { if (File.Exists(getPip)) File.Delete(getPip); }
        }
        if (!File.Exists(pipMarker))
            throw new InvalidOperationException("No se pudo preparar pip para el runtime local de traducción.");
        return python;
    }

    private static void ConfigureManagedPython(string configuration)
    {
        string paths = File.ReadAllText(configuration);
        if (!paths.Contains("Lib\\site-packages", StringComparison.OrdinalIgnoreCase))
            paths += Environment.NewLine + "Lib\\site-packages";
        if (!paths.Contains("import site", StringComparison.Ordinal))
            paths += Environment.NewLine + "import site";
        File.WriteAllText(configuration, paths + Environment.NewLine);
    }

    private static async Task DownloadTranslatorAsset(string url, string destination, long maximumBytes, string expectedSha256 = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await TranslatorHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("El archivo de traducción excede el tamaño permitido.");
        string temporary = destination + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await using (var input = await response.Content.ReadAsStreamAsync())
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory())) > 0)
                {
                    total += read;
                    if (total > maximumBytes) throw new InvalidDataException("El archivo de traducción excede el tamaño permitido.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
                if (expectedSha256 != null &&
                    !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("La verificación del runtime de traducción falló.");
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
