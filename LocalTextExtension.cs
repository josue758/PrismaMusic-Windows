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
        ["Carga una letra antes de traducirla."] = ("Load lyrics before translating them.", "Carregue a letra antes de traduzi-la."),
        ["Usar Argos Translate local"] = ("Use local Argos Translate", "Usar Argos Translate local"),
        ["Elegir Python con Argos"] = ("Choose Python with Argos", "Escolher Python com Argos"),
        ["Idioma de la letra"] = ("Lyrics language", "Idioma da letra"),
        ["La letra se enviará al servidor que indiques."] = ("The lyrics will be sent to the server you specify.", "A letra será enviada ao servidor indicado."),
        ["Traducir al español"] = ("Translate to Spanish", "Traduzir para espanhol"),
        ["Clave API (si el servidor la requiere)"] = ("API key (if required by the server)", "Chave de API (se exigida pelo servidor)"),
        ["Traduciendo…"] = ("Translating…", "Traduzindo…"),
        ["Usa un enlace HTTPS o un servidor local."] = ("Use HTTPS or a local server.", "Use HTTPS ou um servidor local."),
        ["No se pudo traducir la letra."] = ("Could not translate the lyrics.", "Não foi possível traduzir a letra."),
        ["Traducción de letra"] = ("Lyrics translation", "Tradução da letra"),
        ["Elige un Python que tenga Argos Translate instalado."] = ("Choose a Python installation with Argos Translate.", "Escolha uma instalação do Python com Argos Translate."),
        ["Perfil de artista"] = ("Artist profile", "Perfil do artista"),
        ["Wikipedia no encontró perfiles."] = ("Wikipedia did not find any profiles.", "A Wikipédia não encontrou perfis."),
        ["Artículo en Wikipedia"] = ("Wikipedia article", "Artigo na Wikipédia"),
        ["Error al consultar Wikipedia. "] = ("Could not reach Wikipedia. ", "Falha ao consultar a Wikipédia. "),
        ["Elige una pista para transmitir."] = ("Choose a track to cast.", "Escolha uma faixa para transmitir."),
        ["No se pudo usar el selector de dispositivos. "] = ("Could not use the device picker. ", "Não foi possível usar o seletor de dispositivos. "),
        ["No se pudo transmitir la pista. "] = ("Could not cast the track. ", "Não foi possível transmitir a faixa. "),
        ["Dispositivo desconectado."] = ("Device disconnected.", "Dispositivo desconectado."),
        ["Error de transmisión. "] = ("Casting error. ", "Erro de transmissão. "),
        ["No se pudo leer la biblioteca. "] = ("Could not read the library. ", "Não foi possível ler a biblioteca. "),
        ["No se guardó la biblioteca. "] = ("Could not save the library. ", "Não foi possível salvar a biblioteca. "),
        ["Windows no pudo reproducir el archivo. "] = ("Windows could not play the file. ", "O Windows não conseguiu reproduzir o arquivo. "),
        ["No se encontró una letra para esta pista."] = ("No lyrics found for this track.", "Nenhuma letra foi encontrada para esta faixa."),
        ["Error al buscar letras. "] = ("Could not find lyrics. ", "Erro ao buscar letras. "),
        ["Buscar letras"] = ("Find lyrics", "Buscar letras"),
        ["Se enviarán el título, artista y álbum a LRCLIB para buscar la letra."] = ("The title, artist and album will be sent to LRCLIB to find lyrics.", "O título, o artista e o álbum serão enviados ao LRCLIB para buscar a letra."),
        ["Escribe un nombre distinto para la lista."] = ("Choose a different playlist name.", "Escolha outro nome para a lista."),
        ["Salida activa en modo exclusivo."] = ("Exclusive output is active.", "A saída exclusiva está ativa."),
        ["Salida activa en modo compartido."] = ("Shared output is active.", "A saída compartilhada está ativa."),
        ["El modo exclusivo se solicita al abrir una pista."] = ("Exclusive access is requested when you open a track.", "O acesso exclusivo é solicitado ao abrir uma faixa."),
        ["PCM directo exclusivo (WAV y AIFF)"] = ("Exclusive direct PCM (WAV and AIFF)", "PCM direto exclusivo (WAV e AIFF)"),
        ["No se pudieron leer los ajustes de audio. "] = ("Could not read audio settings. ", "Não foi possível ler as configurações de áudio. "),
        ["No se pudieron guardar los ajustes de audio. "] = ("Could not save audio settings. ", "Não foi possível salvar as configurações de áudio. "),
        ["Más opciones"] = ("More options", "Mais opções"),
        ["Aleatorio"] = ("Shuffle", "Aleatório"),
        ["Repetir"] = ("Repeat", "Repetir"),
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
        ["Omitir ecualizador, balance y velocidad"] = ("Bypass equalizer, balance and speed", "Ignorar equalizador, balanço e velocidade"),
        ["Velocidad de reproducción"] = ("Playback speed", "Velocidade de reprodução"),
        ["Apariencia"] = ("Appearance", "Aparência"),
        ["Colores de la carátula"] = ("Artwork colors", "Cores da capa"),
        ["Espectro animado"] = ("Animated spectrum", "Espectro animado"),
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







