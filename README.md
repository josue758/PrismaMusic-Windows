# Prisma Music para Windows

**Versión Windows 0.60.0** · Port nativo para Windows de [Prisma Music para macOS](https://github.com/josue758/PrismaMusic). Comparte el producto y sus funciones, pero usa una interfaz WPF y servicios de audio propios de Windows.

En **Configuración → Biblioteca** o en el menú **…** junto a los filtros, puedes exportar la colección a JSON o las pistas de la vista actual a M3U8. La exportación guarda metadatos y rutas. Los archivos de audio y las carátulas se conservan en sus ubicaciones.

Desde **Configuración → Atajos y acerca de → Buscar actualizaciones** puedes instalar las nuevas versiones. Prisma avisa en la barra superior cuando encuentra una actualización. Esta función está disponible al instalar la versión 0.56.0 o posterior con el instalador .exe.

[Historial completo de versiones Windows](CHANGELOG.md) · [Proyecto para macOS](https://github.com/josue758/PrismaMusic)

Consulta también la [guía del port de Windows](WINDOWS_PORT.md) para conocer sus componentes, diferencias entre plataformas, fuentes de metadatos y proceso de compilación.

Compatible con Windows 10 22H2 y Windows 11, x64. El ejecutable distribuido lleva su propio runtime de .NET.

Esta versión mejora la experiencia de escaneo y la identificación de pistas, muestra los metadatos de etiquetas sin modificar archivos, busca carátulas y perfiles de artistas en línea cuando hace falta, y reorganiza la sala de escucha a pantalla completa y Configuración. El panel de fuentes permite gestionar carpetas locales y recursos NAS/SMB accesibles desde Windows.

Descarga y ejecuta **Prisma-Music-Windows-0.60.0-Setup-x64.exe**. Se instala para tu usuario, incluye .NET y crea un acceso directo en Inicio. Puedes desinstalar Prisma desde Configuración de Windows y conservar la biblioteca en LocalAppData. [Descargas para Windows](https://github.com/josue758/PrismaMusic-Windows/releases)

El asistente de **Primeros pasos** guía en cuatro etapas: idioma, colección, apariencia y preferencias de inicio. Para un NAS, conecta el recurso en el Explorador de archivos y selecciónalo en el asistente. La apariencia inicial incluye tres perfiles de material, tema, color de acento y una vista previa de la transparencia. Pulsa Enter o haz doble clic en una pista para reproducirla, Ctrl+F para buscar y Ctrl+I para ver información.

Incluye canciones, álbumes y artistas, filtros por género y año, búsqueda, agrupación por artista del álbum, favoritos, listas guardadas, cola, historial, metadatos técnicos, carátulas, letras locales y consulta voluntaria en LRCLIB, vista de escucha y reproductor flotante. Cuando faltan recursos locales, la aplicación puede buscar biografías y retratos en Wikipedia/Wikimedia y portadas de álbum en MusicBrainz/Cover Art Archive. Estas búsquedas envían metadatos de artista/álbum, no archivos de audio ni credenciales del NAS. La búsqueda de letras solicita confirmación antes de enviar el título, artista y álbum a LRCLIB.

Para añadir música de un NAS, abre **Fuentes → Conectar NAS**. Escribe el nombre o la IP para listar las carpetas SMB, o usa una ruta como `\\servidor\Música`, una dirección `smb://servidor/Música` o una unidad de red conectada en Windows. Si Windows requiere credenciales, el diálogo ofrece su inicio de sesión. Al terminar el escaneo, la app abre esa fuente en Canciones. Al escanear de nuevo, reutiliza las etiquetas de los archivos que conservan el tamaño y la fecha de modificación.

En **Letras → Traducir**, elige los idiomas de origen y destino. Prisma descarga los modelos compatibles cuando se usan por primera vez. La traducción se ejecuta en el equipo y aparece debajo del verso original también en la Sala de escucha.

El escaneo indica la carpeta y el archivo en proceso, las etiquetas reconocidas y los errores; al terminar informa las pistas nuevas y las etiquetas que actualizó en la biblioteca local. Lee etiquetas, pero no escribe ni retaggea los archivos de música. F11 abre la Sala de escucha con letras y carátula. F12 o el botón «Visualizador» abre su propia vista inmersiva de 48 bandas a pantalla completa. Los formatos reproducibles dependen de los códecs que Windows tenga disponibles.

La barra inferior muestra la pista, su formato, el progreso y los controles de reproducción. El menú de la barra agrupa letras, PiP, pantalla completa, visualizador y transmisión. El temporizador de sueño permite pausar en 15, 30, 60 o 90 minutos. El selector vuelve a enumerar las salidas WASAPI activas cada vez que se abre y muestra la frecuencia, canales, resolución del formato actual y frecuencias PCM compatibles reportadas por el dispositivo. Para WASAPI exclusivo se puede elegir seguimiento automático de la frecuencia de la pista o una frecuencia manual entre las anunciadas para el dispositivo; al elegir una frecuencia distinta se remuestrea la pista y se activa el modo exclusivo. PCM directo exclusivo conserva el formato original compatible de WAV, AIFF o FLAC y no aplica volumen ni remuestreo. Un DAC USB aparece cuando su controlador lo publica como salida habilitada en Windows. Ninguna de estas opciones certifica por sí sola bit-perfect físico. El visualizador a pantalla completa permite elegir barras, espejo, centrado o Aurora, sensibilidad y 24/30/60 FPS. Configuración incluye ecualizador de diez bandas, temporizador y selección de idioma (español, inglés o portugués). Escuchando muestra un espectro de diez bandas calculado a partir del audio. En Álbumes y Artistas, abre un grupo para ver sus pistas. En Listas puedes crear y seleccionar listas con nombre. Se guardan el volumen, el dispositivo, la frecuencia, el modo solicitado, la apariencia y los ajustes del visualizador. Artista permite consultar biografías y miniaturas en Wikipedia. La traducción de letras usa Argos local y modelos descargables; Prisma configura automáticamente su runtime y modelo al solicitar la primera traducción, sin instalar Python manualmente ni enviar letras a servidores. Transmitir abre el selector de Windows para dispositivos que anuncian capacidades de audio o video; la disponibilidad de Miracast y la reproducción dependen del dispositivo y del soporte de Windows. La biblioteca usa SQLite. Las pruebas de hardware y servicios externos siguen siendo manuales. La versión de Mac usa su propia biblioteca.

Para compilar con el SDK .NET 8 en Windows:

    dotnet publish PrismaMusic.csproj -c Release -r win-x64 --self-contained true -o publish

Para generar el instalador, indica al compilador de Inno Setup 7 la versión del proyecto, la ruta absoluta de `publish` y una carpeta de salida. Incluye los avisos de licencia del proyecto y sus dependencias en `publish` antes de compilar.

    ISCC.exe /DAppVersion=0.60.0 "/DPayloadRoot=C:\ruta\publish" "/DSetupOutput=C:\ruta\dist" installer\PrismaMusic.iss

El ejecutable admite --smoke-test para leer las tres demos y comprobar que avanza la reproducción.








En Cola, selecciona una pista para moverla o quitarla. Al cerrar, Prisma Music guarda la cola y la posición de la última pista y reabre en pausa.


Configuración incluye seis preajustes de ecualización, un interruptor de ecualizador y balance estéreo. La salida exclusiva conserva los ajustes DSP.


En Configuración puedes omitir el ecualizador, el balance y el control de velocidad, desactivar el fondo de carátula y ocultar el espectro. El ajuste de volumen y las conversiones que requiera WASAPI siguen activos.


Configuración permite reproducir entre 0,75× y 2× manteniendo el tono con SoundTouch.Net 2.3.2. Código y licencia de la dependencia: https://github.com/owoudenberg/soundtouch.net y https://www.nuget.org/packages/SoundTouch.Net/2.3.2.


PCM directo exclusivo envía WAV, AIFF y FLAC compatibles sin alterar el formato de origen. Omite volumen de aplicación, ecualizador, balance y velocidad. Si el dispositivo rechaza el formato, muestra un error. Prueba realizada en Windows 10 con WAV estéreo de 48 kHz y 16 bits; el modo no certifica por sí solo bit-perfect en todos los dispositivos.


La biblioteca se guarda en SQLite. En la primera apertura, Prisma importa library.json y conserva el archivo original. Los cambios en favoritos e historial se guardan aparte de las pistas.


Para traducir sin enviar letras a un servidor, abre Traducir y selecciona el idioma de la letra. Prisma prepara su propio runtime y descarga Argos Translate y el modelo necesario; no hace falta instalar Python ni elegir un ejecutable. La configuración requiere conexión a Internet la primera vez. Una vez descargado el modelo, la traducción funciona sin conexión.


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


En Traducir, Instalar traducción local prepara el runtime integrado, Argos y el modelo seleccionado. La ruta PCM directa también admite FLAC cuando el descodificador y la salida conservan frecuencia, resolución y canales. La ficha de Wikipedia enlaza a la fuente y muestra la licencia de la imagen cuando está disponible.


Windows 10: escaneo y reproducción SMB verificados por loopback; el reescaneo de una carpeta inaccesible conserva sus referencias. La salida USB Hi-MAX aceptó PCM directo de 48 kHz y 16 bits y avanzó su reloj. Se verificaron nombres y patrones de automatización de los controles principales.


La interfaz incluye una búsqueda rediseñada, duración alineada en las listas, tarjetas con artista y año en líneas separadas y arte de reemplazo por álbum.


Versión 0.30: los botones, las filas, el campo de búsqueda y el control deslizante reaccionan al cursor, a la pulsación y al foco con transiciones cortas. La carátula aparece con un fundido al cambiar de pista. Si Windows tiene desactivadas las animaciones, todo cambia de forma instantánea.

Versión 0.31: diseño Fluent de Windows 11. En Windows 11 22H2 o posterior la ventana usa el fondo Mica del sistema; en Windows 10 conserva un fondo sólido neutro. Usa Segoe UI Variable, Segoe Fluent Icons, superficies translúcidas neutras y el color de acento de Windows. La biblioteca SQLite hace copias de seguridad diarias, verifica su integridad al abrir y restaura la copia más reciente si detecta daños.

Versión 0.32: los controles tienen superficies más redondeadas y reflejos translúcidos que siguen el cursor; un resplandor responde al nivel del audio. La paleta de acento y el fondo se adaptan a los colores dominantes de la carátula. Las carátulas se guardan una vez en SQLite y se comparten entre las pistas que contienen la misma imagen. El botón Cancelar escaneo interrumpe el recorrido de carpetas sin reemplazar la biblioteca anterior por resultados incompletos.

Versión 0.33: rediseño completo de la ventana con fondo ambiental desenfocado desde la carátula, paneles de vidrio, navegación más clara, arte destacado y controles de reproducción ampliados. Traducir prepara automáticamente Python integrado y Argos Translate; no requiere instalar Python ni seleccionar un ejecutable. El runtime oficial se verifica mediante SHA-256, se descarga desde python.org, y las traducciones se ejecutan sin conexión después de instalar cada modelo. Al elegir un retrato, Windows detecta localmente el rostro más prominente y genera un recorte cuadrado centrado para la biblioteca.

La suite automatizada está en `PrismaMusic.Tests`. El flujo de CI de Windows compila y ejecuta las pruebas para los cambios enviados al repositorio; consulta su configuración antes de asumir que hay binarios publicados. Las pruebas de audio cubren la selección automática/manual de frecuencia y verifican que PCM directo y WASAPI compartido no reciban una frecuencia exclusiva solicitada.

