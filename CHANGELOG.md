# Historial de Prisma Music para Windows

Este historial documenta las versiones de Windows para las que se encontró código fuente o metadatos de versión verificables. La edición Windows es el port oficial de [Prisma Music para macOS](https://github.com/josue758/PrismaMusic); las interfaces y el motor de audio son nativos de cada sistema y sus números de versión no tienen que coincidir.

## 0.62.4

- La ficha de artista ajusta las columnas al ancho de la ventana y al tamaño de portada elegido en Apariencia. Las portadas conservan su forma cuadrada y se cargan con mayor resolución.
- Las canciones destacadas pueden ocupar dos columnas para dejar espacio a los álbumes en ventanas amplias.
- Los accesos Canciones y Álbumes muestran sus atajos de teclado. Volver desde un álbum recupera el foco de su tarjeta y conserva los álbumes que ya se mostraban.

## 0.62.3

- El informe de escaneo muestra el estado final, permite buscar rutas por nombre o ubicación y muestra el detalle al seleccionar un error.
- Puedes reintentar una ruta o todas las fallidas desde el informe. Prisma conserva los resultados anteriores y los errores que aún quedan por resolver.

## 0.62.2

- **Reubicar fuente** permite elegir la carpeta o el archivo desde Windows, o pegar una ruta SMB.
- La verificación muestra el progreso y permite cancelar. Prisma aplica el cambio solo cuando lo confirmas tras verificar la ubicación.

## 0.62.1

- Las ediciones nuevas guardan solo los campos corregidos. Al volver a escanear, Prisma puede incorporar otros cambios hechos en el archivo.
- El escaneo actualiza las referencias de la cola y el reproductor. La ficha de información muestra las correcciones guardadas en Prisma.
- Reubicar una fuente conserva las carátulas guardadas y la opción de deshacer la última edición por lotes.

## 0.62.0

- **Reubicar fuente** verifica la nueva ruta de una fuente y conserva favoritos, listas, etiquetas y reproducciones.
- La selección múltiple permite editar etiquetas por lotes, previsualizar la numeración y deshacer el último lote.
- El informe del escaneo muestra las rutas nuevas, actualizadas y fallidas. Las rutas con error se pueden reintentar por separado.
- Las fichas de artista comparten un índice de álbumes, adaptan las carátulas al ancho y permiten saltar entre canciones y álbumes con el teclado.

## 0.61.1

- El editor de etiquetas permite editar artista del álbum, disco y pista, y recuperar la información del archivo.
- Las miniaturas de las canciones tienen su propia cola de carga. Prisma también reconoce portadas cover o folder en la carpeta del álbum.
- Las carátulas de archivos modificados se renuevan al escanear. La caché de búsquedas de carátulas en línea tiene límites y los fallos de red se pueden reintentar.
- Historial y listas reutilizan el índice de rutas y el escaneo agrupa las actualizaciones de la vista.

## 0.61.0

- La galería de Artistas abre una ficha con retrato, biografía, canciones destacadas y álbumes de la biblioteca.
- Las canciones más reproducidas se ordenan por el historial de reproducciones contado por Prisma. La ficha también permite abrir la lista completa.
- Los álbumes se pueden abrir desde sus carátulas y el retrato se puede cambiar desde la ficha.

## 0.60.2

- La lista compacta conserva artista, álbum, año y bitrate. La ficha de un álbum muestra los números de disco y pista y reduce las columnas repetidas.
- La búsqueda de canciones reutiliza el orden de la biblioteca. Volver restaura la selección usando la identidad de la pista o colección.
- Si falla la apertura de una pista del NAS, se puede reintentar conservando la posición solicitada y el estado de reproducción.

## 0.60.1

- Identifica ALAC dentro de archivos M4A y lee su resolución, frecuencia, canales y bitrate medio de audio.
- Unifica los datos de formato entre la biblioteca, el reproductor y la información del archivo. La columna de formato muestra el códec y el bitrate.
- Completa los datos técnicos de pistas antiguas al actualizar la biblioteca.

## 0.60.0

- Lee rápidamente etiquetas FLAC y omite la transferencia de carátulas incrustadas durante el escaneo.
- Carga retratos acreditados de Wikimedia en la galería y en la ficha del artista, con las portadas del álbum como alternativa.
- Permite elegir idioma original y de destino para traducir con modelos locales. Muestra original y traducción juntos tanto en las letras sincronizadas como en las simples.

## 0.59.1

- Guarda las pistas nuevas durante el escaneo y conserva el progreso cuando se cancela.
- Añade «Escanear todas las subcarpetas» al menú de cada fuente.

## 0.59.0

- Permite introducir el nombre o la IP de un NAS, ver sus carpetas SMB y abrir el inicio de sesión de Windows si el recurso lo requiere.
- Rediseña el diálogo de conexión y distingue los errores de acceso, las carpetas no disponibles y las fuentes conectadas sin archivos de audio compatibles.
- Omite los archivos auxiliares de macOS en bibliotecas SMB para que no se cuenten como pistas dañadas.
- Añade «Importar solo pistas nuevas» a las fuentes, reutiliza los datos de búsqueda y suaviza las transiciones de biblioteca.

## 0.58.0

- Rediseña la bienvenida en cuatro pasos con selección de idioma, archivos y NAS, vista previa de apariencia y preferencias de inicio.
- Ordena los accesos del reproductor como EQ, salida de audio, Miracast, PiP y Sala de escucha.
- Comienza a leer etiquetas mientras se enumeran los archivos y permite configurar el número de lecturas paralelas, las subcarpetas y el escaneo al iniciar.

## 0.57.3

- Espera a que todas las instancias de Prisma de la instalación de destino cierren antes de reemplazar archivos. Esto corrige el error de acceso denegado al actualizar desde versiones anteriores.

## 0.57.2

- Usa una onda de audio para identificar el Visualizador en la barra superior y el menú de reproducción.

## 0.57.1

- Distingue el Visualizador a pantalla completa (F12) de la Sala de escucha (F11) con accesos separados y etiquetas visibles.

## 0.57.0

- Añade Atrás a la barra superior y el atajo Alt + ← para recuperar la vista anterior con sus filtros, página y selección.
- Rediseña el visualizador con una carátula independiente, una línea de tiempo interactiva y el modo Aurora. El espectro se dibuja en una sola superficie con transiciones suavizadas.

## 0.56.1

- Encaja la ventana maximizada en el área de trabajo de su monitor para que la barra de tareas de Windows no tape los controles de reproducción. La Sala de escucha utiliza el monitor completo.

## 0.56.0

- Busca nuevas versiones en segundo plano y muestra «Actualizar» cuando hay un instalador disponible.
- Permite buscar, descargar y verificar actualizaciones desde Configuración. El instalador vuelve a abrir Prisma Music al terminar.

## 0.55.0

- Usa el logo de Prisma Music en los ejecutables de Windows, el instalador, el acceso directo, la ventana y la barra de tareas.
- Acelera la lectura de etiquetas por SMB con concurrencia limitada. Los archivos sin cambios pasan por el escaneo sin ocupar los lectores de etiquetas.
- Evita guardar y redibujar la biblioteca cuando el índice ya está al día. Muestra el progreso y la acción de cancelar juntos en la biblioteca.

## 0.54.1

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
