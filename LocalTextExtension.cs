using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Markup;

namespace PrismaMusic;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocalTextExtension : MarkupExtension
{
    private static readonly string LanguageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrismaMusic.Windows", "language.txt");
    public static string Language { get; } = ReadLanguage();
    public string Text { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Translate(Text);

    public static string Translate(string spanish)
    {
        if (Language == "es" || !Translations.TryGetValue(spanish, out var pair)) return spanish;
        return Language == "pt" ? pair.Portuguese : pair.English;
    }

    public static void SaveLanguage(string language)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LanguageFile));
        File.WriteAllText(LanguageFile + ".tmp", language);
        File.Move(LanguageFile + ".tmp", LanguageFile, true);
    }

    private static string ReadLanguage()
    {
        var args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "--smoke-test") >= 0)
        {
            int index = Array.IndexOf(args, "--language");
            if (index >= 0 && index + 1 < args.Length && args[index + 1] is "es" or "en" or "pt")
                return args[index + 1];
        }
        return ReadSavedLanguage();
    }

    private static string ReadSavedLanguage()
    {
        try
        {
            var value = File.ReadAllText(LanguageFile).Trim();
            if (value is "es" or "en" or "pt") return value;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        var system = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return system is "en" or "pt" ? system : "es";
    }

    private static readonly Dictionary<string, (string English, string Portuguese)> Translations = new()
    {
        ["BIBLIOTECA LOCAL"] = ("LOCAL LIBRARY", "BIBLIOTECA LOCAL"),
        ["＋  Carpeta"] = ("＋  Folder", "＋  Pasta"),
        ["↻  Actualizar"] = ("↻  Refresh", "↻  Atualizar"),
        ["⚙  Configuración"] = ("⚙  Settings", "⚙  Configurações"),
        ["Subir en la cola"] = ("Move up in queue", "Subir na fila"),
        ["Bajar en la cola"] = ("Move down in queue", "Descer na fila"),
        ["Quitar"] = ("Remove", "Remover"),
        ["Canciones"] = ("Songs", "Músicas"),
        ["Álbumes"] = ("Albums", "Álbuns"),
        ["Artistas"] = ("Artists", "Artistas"),
        ["Favoritos"] = ("Favorites", "Favoritos"),
        ["Cola"] = ("Queue", "Fila"),
        ["Historial"] = ("History", "Histórico"),
        ["Listas"] = ("Playlists", "Listas"),
        ["TU COLECCIÓN"] = ("YOUR COLLECTION", "SUA COLEÇÃO"),
        ["Buscar canción, artista, álbum, género o año"] = ("Search song, artist, album, genre or year", "Buscar música, artista, álbum, gênero ou ano"),
        ["Buscar música"] = ("Search music", "Buscar música"),
        ["Nueva lista"] = ("New playlist", "Nova lista"),
        ["‹ Volver"] = ("‹ Back", "‹ Voltar"),
        ["Nombre"] = ("Name", "Nome"),
        ["Artista"] = ("Artist", "Artista"),
        ["Año"] = ("Year", "Ano"),
        ["Género"] = ("Genre", "Gênero"),
        ["Orden"] = ("Sort", "Ordenar"),
        ["Escuchando"] = ("Now playing", "Reproduzindo"),
        ["Favorito"] = ("Favorite", "Favorito"),
        ["＋ Cola"] = ("＋ Queue", "＋ Fila"),
        ["＋ Lista"] = ("＋ Playlist", "＋ Lista"),
        ["Añadir o quitar de la lista seleccionada"] = ("Add to or remove from the selected playlist", "Adicionar à lista selecionada ou remover dela"),
        ["Información del archivo"] = ("File information", "Informações do arquivo"),
        ["Letras web"] = ("Find lyrics", "Buscar letras"),
        ["Traducir"] = ("Translate", "Traduzir"),
        ["Transmitir"] = ("Cast", "Transmitir"),
        ["Desconectar"] = ("Disconnect", "Desconectar"),
        ["Pantalla completa"] = ("Full screen", "Tela cheia"),
        ["Espectro de la pista"] = ("Track spectrum", "Espectro da faixa"),
        ["Espectro de diez bandas"] = ("Ten-band spectrum", "Espectro de dez bandas"),
        ["Anterior"] = ("Previous", "Anterior"),
        ["Reproducir o pausar"] = ("Play or pause", "Reproduzir ou pausar"),
        ["Siguiente"] = ("Next", "Próxima"),
        ["Volumen"] = ("Volume", "Volume"),
        ["Posición"] = ("Position", "Posição"),
        ["Plano"] = ("Flat", "Plano"),
        ["Clásica"] = ("Classical", "Clássica"),
        ["Electrónica"] = ("Electronic", "Eletrônica"),
        ["Activar ecualizador"] = ("Enable equalizer", "Ativar equalizador"),
        ["Balance estéreo"] = ("Stereo balance", "Balanço estéreo"),
        ["Centrar"] = ("Center", "Centralizar"),
        ["Ecualizador"] = ("Equalizer", "Equalizador"),
        ["Diez bandas · −12 a +12 dB"] = ("Ten bands · −12 to +12 dB", "Dez bandas · −12 a +12 dB"),
        ["Salida de audio · WASAPI"] = ("Audio output · WASAPI", "Saída de áudio · WASAPI"),
        ["Predeterminada del sistema"] = ("System default", "Padrão do sistema"),
        ["Solicitar acceso exclusivo"] = ("Request exclusive access", "Solicitar acesso exclusivo"),
        ["Pausar en 30 minutos"] = ("Pause in 30 minutes", "Pausar em 30 minutos"),
        ["Cancelar temporizador"] = ("Cancel timer", "Cancelar temporizador"),
        ["Configuración · Prisma Music"] = ("Settings · Prisma Music", "Configurações · Prisma Music"),
        ["Idioma de interfaz"] = ("Interface language", "Idioma da interface"),
        ["El idioma se aplicará al volver a abrir Prisma Music."] = ("The language will change when you reopen Prisma Music.", "O idioma será aplicado ao reabrir o Prisma Music."),
        ["Todos los géneros"] = ("All genres", "Todos os gêneros"),
        ["Todos los años"] = ("All years", "Todos os anos"),
        ["Sin letras disponibles"] = ("No lyrics available", "Sem letras disponíveis"),
        ["{0:N0} canciones en tu biblioteca"] = ("{0:N0} songs in your library", "{0:N0} músicas na sua biblioteca"),
        ["Leyendo carpetas…"] = ("Reading folders…", "Lendo pastas…"),
        ["Crear"] = ("Create", "Criar"),
        ["Nombre de la lista"] = ("Playlist name", "Nome da lista")
    };
}



