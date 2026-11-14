# Prisma Music para Windows

Vista previa 0.10.0 para Windows 10 22H2 y Windows 11, x64. Port de Prisma Music para macOS en C# y WPF. El ejecutable distribuido lleva su propio runtime de .NET.

Extrae el ZIP y abre **Instalar.cmd**. La instalación es por usuario y crea un acceso directo en Inicio. También puedes abrir **App/Prisma Music.exe** directamente. La opción de desinstalación de Windows conserva la biblioteca en LocalAppData.

Usa **Demos** para probar tres pistas incluidas, o **Carpeta** para agregar música local o una unidad SMB. Para un NAS, conecta el recurso en el Explorador de archivos y selecciónalo desde la app. Pulsa Enter o haz doble clic en una pista para reproducirla, Ctrl+F para buscar, Ctrl+I para ver información y F11 para abrir la vista de escucha.

Incluye canciones, álbumes y artistas, filtros por género y año, búsqueda, agrupación por artista del álbum, favoritos, listas guardadas, cola, historial, metadatos técnicos, carátulas, letras locales y consulta voluntaria en LRCLIB, vista de escucha y reproductor flotante. Los formatos reproducibles dependen de los códecs que Windows tenga disponibles.

Configuración incluye ecualizador de diez bandas, selector WASAPI compartido o exclusivo con retorno automático al modo compartido cuando el dispositivo rechaza el acceso exclusivo y temporizador. Escuchando muestra un espectro de diez bandas calculado a partir del audio. En Álbumes y Artistas, abre un grupo para ver sus pistas. En Listas puedes crear y seleccionar listas con nombre. Se guardan el volumen, el dispositivo, el modo solicitado y el ecualizador. Artista permite consultar biografías y miniaturas en Wikipedia. Traducir muestra el original y la traducción al español y requiere un servidor LibreTranslate configurado por el usuario. Transmitir abre el selector de Windows para enviar una pista a un receptor de audio compatible. Desconectar finaliza la sesión. Quedan pendientes la prueba con un receptor físico. Configuración permite elegir español, inglés o portugués para la navegación y los controles principales. Algunos mensajes y diálogos siguen en español. La biblioteca usa JSON y aún falta validar colecciones grandes, un NAS real y Windows 11. La versión de Mac usa su propia biblioteca.

Para compilar con el SDK .NET 8 en Windows:

    dotnet publish PrismaMusic.csproj -c Release -r win-x64 --self-contained true -o publish

El ejecutable admite --smoke-test para leer las tres demos y comprobar que avanza la reproducción.








En Cola, selecciona una pista para moverla o quitarla. Al cerrar, Prisma Music guarda la cola y la posición de la última pista y reabre en pausa.


Configuración incluye seis preajustes de ecualización, un interruptor de ecualizador y balance estéreo. La salida exclusiva conserva los ajustes DSP.

