# Historial de Prisma Music para Windows

Este historial documenta las versiones de Windows para las que se encontró código fuente o metadatos de versión verificables. La edición Windows es el port oficial de [Prisma Music para macOS](https://github.com/josue758/PrismaMusic); las interfaces y el motor de audio son nativos de cada sistema y sus números de versión no tienen que coincidir.

## 0.37.0

- Rediseña el panel lateral de letras, con búsqueda directa y ajustes de tamaño y sincronización de hasta ±10 segundos para el panel y la sala de escucha.
- Permite pausar al final de la canción desde el menú del reproductor. Configuración permite desactivar el avance automático de forma persistente.

## 0.36.0

- Rediseña la barra inferior con iconos Fluent, portada, artista, formato de audio y progreso centrado con tiempo transcurrido y restante.
- Añade silencio con restauración del volumen y un temporizador de 15, 30, 60 o 90 minutos con cuenta regresiva.
- Agrupa letras, PiP, pantalla completa, visualizador y transmisión en el menú de la barra.

## 0.35.0

- Agrupa las acciones de letras, pista y vista en el menú del reproductor.
- Destaca el verso activo y diferencia su traducción en las letras sincronizadas.
- Mejora los controles del minirreproductor flotante y conserva su ventana al minimizar la principal.
- Añade edición de título, artista, álbum, género y año en la biblioteca. Las correcciones sobreviven a los escaneos y se guardan en Prisma Music. La ficha del archivo muestra las etiquetas originales y el formato de audio.

## 0.34.0

- Añade progreso del escaneo con carpeta, archivo, etiquetas leídas, pistas encontradas y errores; informa los cambios al terminar. Las etiquetas se leen sin escribir en los archivos.
- Permite consultar etiquetas completas de pista, artista del álbum, año, género, número de pista/disco y presencia de carátula integrada.
- Busca portadas de álbum faltantes mediante MusicBrainz y Cover Art Archive; muestra fotografías y biografías de artista consultadas en Wikipedia/Wikimedia.
- Reorganiza la sala de escucha a pantalla completa para adaptar la carátula y las letras al espacio disponible, y ofrece buscar letras cuando no hay ninguna cargada.
- Añade botones de regreso visibles para fichas y Configuración; Configuración presenta secciones guiadas, funciona con Escape y guarda cambios mientras se editan.
- Mantiene la exploración de fuentes locales y carpetas NAS/SMB accesibles en Windows, así como PiP, biblioteca, perfiles, salida de audio y traducción local.

## 0.33.0

- Rediseña la ventana con fondo ambiental desenfocado desde la carátula, paneles translúcidos, navegación y controles de reproducción ampliados.
- Prepara Python integrado y Argos Translate automáticamente; tras descargar el modelo, la traducción funciona sin conexión.
- Centra localmente el retrato elegido en el rostro más prominente.

## 0.32.0

- Añade superficies redondeadas, reflejos que siguen el cursor y un resplandor que responde al audio.
- Adapta acento y fondo a los colores de la carátula.
- Guarda carátulas compartidas en SQLite y permite cancelar escaneos sin reemplazar la biblioteca por resultados incompletos.

## 0.31.0

- Introduce una interfaz Fluent basada en Windows, con fondo Mica en Windows 11 y fondo neutro en Windows 10.
- Añade copias de seguridad diarias de SQLite, comprobación de integridad y restauración tras daños.

## 0.30.0

- Añade transiciones de interacción en botones, filas, búsqueda y controles de reproducción, respetando la preferencia del sistema para reducir animaciones.
- Presenta fundidos breves al cambiar de carátula.

## Fuentes de versión

- Versión actual: `PrismaMusic.csproj` y `Install.ps1`.
- Versiones anteriores verificables: historial de funciones 0.30.0–0.33.0 en el README del proyecto y archivo fuente 0.31.0 disponible localmente.
- Para asociar binarios descargables con cada versión, añadir las etiquetas y archivos de distribución originales a GitHub Releases.
