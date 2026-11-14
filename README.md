# Prisma Music para Windows

Vista previa 0.27.0 para Windows 10 22H2 y Windows 11, x64. Port de Prisma Music para macOS en C# y WPF. El ejecutable distribuido lleva su propio runtime de .NET.

Extrae el ZIP y abre **Instalar.cmd**. La instalación es por usuario y crea un acceso directo en Inicio. También puedes abrir **App/Prisma Music.exe** directamente. La opción de desinstalación de Windows conserva la biblioteca en LocalAppData.

Usa **Demos** para probar tres pistas incluidas, o **Carpeta** para agregar música local o una unidad SMB. Para un NAS, conecta el recurso en el Explorador de archivos y selecciónalo desde la app. Pulsa Enter o haz doble clic en una pista para reproducirla, Ctrl+F para buscar, Ctrl+I para ver información y F11 para abrir la vista de escucha.

Incluye canciones, álbumes y artistas, filtros por género y año, búsqueda, agrupación por artista del álbum, favoritos, listas guardadas, cola, historial, metadatos técnicos, carátulas, letras locales y consulta voluntaria en LRCLIB, vista de escucha y reproductor flotante. Los formatos reproducibles dependen de los códecs que Windows tenga disponibles.

Configuración incluye ecualizador de diez bandas, selector WASAPI compartido o exclusivo con retorno automático al modo compartido cuando el dispositivo rechaza el acceso exclusivo y temporizador. Escuchando muestra un espectro de diez bandas calculado a partir del audio. En Álbumes y Artistas, abre un grupo para ver sus pistas. En Listas puedes crear y seleccionar listas con nombre. Se guardan el volumen, el dispositivo, el modo solicitado y el ecualizador. Artista permite consultar biografías y miniaturas en Wikipedia. Traducir muestra el original y la traducción al español con Argos Translate local o un servidor LibreTranslate. Transmitir abre el selector de Windows para enviar una pista a un receptor de audio compatible. Desconectar finaliza la sesión. Quedan pendientes la prueba con un receptor físico. Configuración permite elegir español, inglés o portugués para la navegación y los controles principales. Algunos mensajes y diálogos siguen en español. La biblioteca usa JSON y aún falta validar colecciones grandes, un NAS real y Windows 11. La versión de Mac usa su propia biblioteca.

Para compilar con el SDK .NET 8 en Windows:

    dotnet publish PrismaMusic.csproj -c Release -r win-x64 --self-contained true -o publish

El ejecutable admite --smoke-test para leer las tres demos y comprobar que avanza la reproducción.








En Cola, selecciona una pista para moverla o quitarla. Al cerrar, Prisma Music guarda la cola y la posición de la última pista y reabre en pausa.


Configuración incluye seis preajustes de ecualización, un interruptor de ecualizador y balance estéreo. La salida exclusiva conserva los ajustes DSP.


En Configuración puedes omitir el ecualizador, el balance y el control de velocidad, desactivar el fondo de carátula y ocultar el espectro. El ajuste de volumen y las conversiones que requiera WASAPI siguen activos.


Configuración permite reproducir entre 0,75× y 2× manteniendo el tono con SoundTouch.Net 2.3.2. Código y licencia de la dependencia: https://github.com/owoudenberg/soundtouch.net y https://www.nuget.org/packages/SoundTouch.Net/2.3.2.


PCM directo exclusivo envía WAV y AIFF PCM en su formato original. Omite volumen de aplicación, ecualizador, balance y velocidad. Si el dispositivo rechaza el formato, muestra un error. Prueba realizada en Windows 10 con WAV estéreo de 48 kHz y 16 bits.


La biblioteca se guarda en SQLite. En la primera apertura, Prisma importa library.json y conserva el archivo original. Los cambios en favoritos e historial se guardan aparte de las pistas.


Para traducir sin enviar letras a un servidor, instala Argos Translate en un entorno Python y descarga los modelos de idioma a español que necesites. En Traducir, elige el python.exe de ese entorno y el idioma de la letra. Prisma recuerda el ejecutable después de la primera traducción. Los modelos y Python se instalan por separado.


Si la letra tiene marcas de tiempo, Argos traduce sus líneas y Escuchando muestra el original y la traducción sincronizados. Las letras sin marcas se muestran en la ventana de traducción.


Escuchando, pantalla completa y Mini muestran el formato, la resolución disponible y la frecuencia de la pista. Mini también muestra la carátula y el artista.


La barra inferior incluye reproducción aleatoria y repetición de la colección. En Escuchando, el menú de tres puntos reúne traducción, perfil de artista, Mini y pantalla completa.


El botón Repetir alterna entre desactivado, repetir colección y repetir pista. El modo de repetición, la reproducción aleatoria y el orden de la sesión se guardan al cerrar.


Pulsa la carátula para abrir o cerrar pantalla completa. En PCM directo, Activar volumen vuelve a la salida ajustable. Configuración permite conservar o reiniciar la posición de la última pista al reabrir.


La sala Escuchando abre en una ventana de pantalla completa independiente. Comparte reproducción, carátula, formato y letras con la ventana principal. Pulsa Esc, F11 o Volver para salir.


Desde la ficha de un álbum o artista, selecciona una pista y usa Ver artista o Ver álbum. Volver recupera la ficha y la selección anteriores. El perfil en línea consulta Wikipedia en el idioma de la interfaz y recurre al inglés si no encuentra coincidencias. El perfil muestra los recuentos de la biblioteca aunque falle la consulta.


Las fichas de álbum y artista incluyen carátula de la colección cuando está disponible, duración y reproducción directa o aleatoria. La ficha de álbum numera sus pistas en orden de disco y canción.


Álbumes y Artistas se muestran en cuadrículas de 30 tarjetas por página. En el perfil de Wikipedia pulsa Usar este retrato para guardarlo en el equipo. En Escuchando o en la sala de pantalla completa, pulsa una línea de la letra sincronizada para saltar a ese verso.


Primeros pasos permite añadir una carpeta o probar las demos. La búsqueda de letras consulta LRCLIB con alternativas y comprueba el título, el artista, la duración y la versión antes de mostrar un resultado.


La preparación del archivo de audio corre en segundo plano. Durante la apertura, pulsa × o Esc para descartar la solicitud, o selecciona otra pista. Una llamada de almacenamiento que ya está en curso puede tardar en devolver el control al cargador.


En Traducir, Instalar traducción local prepara Argos y descarga el modelo seleccionado. Requiere Python 3.12. La ruta PCM directa también admite FLAC cuando el descodificador y la salida conservan frecuencia, resolución y canales. La ficha de Wikipedia enlaza a la fuente y muestra la licencia de la imagen cuando está disponible.

