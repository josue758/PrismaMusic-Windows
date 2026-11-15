# Historial de Prisma Music para Windows

Este historial documenta las versiones de Windows para las que se encontró código fuente o metadatos de versión verificables. La edición Windows es el port oficial de [Prisma Music para macOS](https://github.com/josue758/PrismaMusic); las interfaces y el motor de audio son nativos de cada sistema y sus números de versión no tienen que coincidir.

## 0.54.0

- Aprovecha las ventanas amplias con columnas de álbum y año, paneles proporcionados y portadas adaptables en Inicio.
- Acepta rutas SMB, UNC y unidades de red conectadas. Abre la fuente indexada en Canciones.
- Escanea en paralelo con límite de concurrencia, reutiliza metadatos de archivos sin cambios y evita reconstrucciones de la biblioteca mientras se escribe una búsqueda.

## 0.53.0

- Separa la navegación, biblioteca, panel de escucha y controles de reproducción en superficies Fluent con bordes y espaciado comunes.
- Rediseña Inicio con una pista destacada, portadas que se adaptan a la ventana y accesos a canciones recientes.
- Añade un indicador de la pista actual en la biblioteca y simplifica la barra en pantalla completa.

## 0.52.0

- Amplía Apariencia con controles de movimiento, paleta de carátula, colores de acento, temas y materiales ajustables.
- Acerca la Sala de escucha a macOS con una portada de mayor tamaño, letras en una columna propia y un acceso directo a la biblioteca.

## 0.51.0

- Completa Sonido con un medidor dBFS, balance estéreo y el selector de dispositivos WASAPI con actualización manual.
- Muestra el dispositivo solicitado y el activo, el formato real y la ruta de frecuencia. Agrupa PCM directo, acceso exclusivo y transmisión inalámbrica en el mismo panel.

## 0.50.0

- Reorganiza Configuración en siete secciones con tarjetas y navegación lateral de estilo Fluent.
- Añade al ecualizador una gráfica de las bandas, preamplificación y compensación automática ajustable.
- Integra el temporizador de sueño, las fuentes de biblioteca y la vista previa de tamaño de letras.

## 0.49.0

- Añade accesos directos a visualizador, letras, cola e información junto a la búsqueda.
- Amplía la vista de la cola con portada, duración y acciones por canción. El acceso de cola abre y cierra el panel sin cambiar de sección.

## 0.48.0

- Añade accesos directos al ecualizador, al selector de transmisión y al minirreproductor PiP en la barra inferior.
- Añade Inicio con álbumes destacados, cifras de la biblioteca y canciones recientes.

## 0.47.0

- Integra la búsqueda en la barra de título y organiza navegación, biblioteca y reproducción en superficies continuas.
- Aplica Mica cuando Windows lo admite y un fondo sólido cuando no está disponible.
- Unifica Bienvenida, Configuración, salida de audio y sala de escucha con la misma paleta y controles.

## 0.46.0

- Añade pestañas de letras, cola e información y permite plegar el panel de reproducción.
- Permite reordenar la cola lateral con el ratón o los botones y deshacer la última eliminación.
- Permite iniciar en Canciones, Álbumes o la última sección y recuperar filtros al volver de una ficha.
- El selector de salida muestra las frecuencias de origen y salida y avisa cuando difieren o se usa la salida predeterminada.
- Agrupa reproducción, navegación y metadatos en el menú contextual de las canciones.

## 0.45.0

- Integra los controles multimedia de Windows con el motor local, incluidos reproducir, pausar, anterior, siguiente y cambiar de posición.
- Publica el título, artista, álbum y estado de la pista en la sesión multimedia de Windows.

## 0.44.0

- Ajusta el tamaño y las columnas de la galería al ancho disponible.
- Intenta recuperar la reproducción por WASAPI cuando Windows invalida la salida. Conserva la posición y la selección de dispositivo. PCM directo conserva su requisito de salida exclusiva.

## 0.43.0

- Añade estados vacíos con acciones, tres tamaños de portada y etiquetas visibles para Cola y Lista.
- Simplifica Bienvenida y traslada estilo, FPS y sensibilidad del visualizador a Configuración.
- Separa las búsquedas automáticas de carátulas y letras, y permite evitar el reposo durante la reproducción.
- Muestra el dispositivo que reproduce el audio y la salida solicitada cuando difieren.

## 0.42.0

- Rediseña la salida de audio como un panel con dispositivo, señal activa, modos WASAPI y frecuencias compatibles.
- Consulta las salidas en segundo plano y conserva los controles de actualización, frecuencia exclusiva y PCM directo.
- Unifica las superficies y los iconos de la biblioteca y el panel de reproducción.

## 0.41.0

- Unifica los iconos de la navegación y añade interruptores Fluent en Configuración y Bienvenida.
- Añade filas compactas, visibilidad de formato y una opción para mantener el minirreproductor flotante.
- Permite leer las letras completas sin seguimiento, carga archivos laterales TXT y conserva el salto por verso para LRC.
- Añade Reproducir a continuación desde el menú de una pista. Mueve una pista ya en cola al primer lugar.

## 0.40.0

- Añade la página Biblioteca en Configuración y un menú junto a los filtros de la colección.
- Exporta pistas, fuentes, listas y etiquetas editadas a JSON, o las pistas de la vista filtrada a M3U8.
- Ajusta el espaciado de las tarjetas de álbum y artista para mostrar más portadas.

## 0.39.0

- Rediseña la biblioteca con superficies más neutras, una jerarquía de lectura más clara y un indicador de formato por canción.
- Añade la vista Sin pérdida de macOS para pistas FLAC, ALAC y PCM identificadas al escanear. Usa Actualizar biblioteca para completar los formatos de archivos importados antes de esta versión.
- Combina filtros por artista, género y año, permite buscar varias palabras sin exigir tildes y añade el orden por álbum.

## 0.38.0

- Simplifica Bienvenida con pasos claros, un resumen y tarjetas de apariencia.
- Divide Configuración en páginas de Sonido, Reproducción, Letras, Apariencia e Idioma.
- Adapta los perfiles Cristal, Sereno y Contraste de macOS a la interfaz de Windows.

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
