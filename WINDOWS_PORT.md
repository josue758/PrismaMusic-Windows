# Guía del port para Windows

Prisma Music para Windows es la edición Windows del reproductor Prisma Music para macOS. Comparte el producto y las funciones principales, pero su interfaz, reproducción y acceso a dispositivos se implementan con tecnologías nativas de Windows. Las versiones de ambas ediciones se numeran por separado.

## Componentes

- **Interfaz:** WPF sobre .NET 8, con diseño adaptado a Windows 10 y Windows 11.
- **Audio:** NAudio y WASAPI para enumerar salidas, reproducir en modo compartido o exclusivo y gestionar PCM directo cuando el formato del archivo y el dispositivo son compatibles.
- **Biblioteca:** SQLite para pistas, colecciones y estado local. Las etiquetas integradas en los archivos se leen, no se modifican.
- **Fuentes:** carpetas locales y recursos NAS/SMB accesibles desde Windows; para mayor fiabilidad, conecta primero el recurso desde el Explorador.
- **Letras y traducción:** letras locales y búsqueda voluntaria en LRCLIB; traducción local con Argos Translate después de descargar su runtime y modelo.

## Datos y servicios en línea

Las búsquedas de carátulas usan MusicBrainz y Cover Art Archive. Los perfiles consultan Wikipedia y Wikimedia para biografías e imágenes. La búsqueda de letras requiere confirmación antes de enviar el título y los metadatos de la pista a LRCLIB. Estas funciones envían metadatos de búsqueda, no audio ni credenciales del NAS.

La aplicación conserva la biblioteca, las preferencias y los recursos guardados en el almacenamiento local de Windows. Desinstalar la aplicación no elimina la biblioteca. Las traducciones se realizan en el equipo una vez descargados los componentes; no se envían letras a un servicio remoto para traducirlas.

## Diferencias frente a macOS

Windows usa WPF y los dispositivos de audio expuestos por Windows; macOS tiene su propia interfaz, motor y biblioteca. El selector WASAPI, PCM directo, integración con recursos SMB y los detalles de instalación son específicos de Windows. Que una función esté disponible en una edición no implica que ambas compartan el código, los datos locales o el mismo número de versión.

## Versionado de Windows

El historial verificable comienza en 0.30.0 y se detalla en [CHANGELOG.md](CHANGELOG.md). La versión actual se define en `PrismaMusic.csproj` y en el instalador `Install.ps1`. Los números históricos describen cambios documentados; no garantizan que haya un instalador descargable para cada versión.

## Compilación y pruebas

Requiere Windows y el SDK de .NET 8. Desde la carpeta del proyecto:

```powershell
dotnet publish PrismaMusic.csproj -c Release -r win-x64 --self-contained true -o publish
```

La suite está dentro de `PrismaMusic.Tests`. Desde la raíz de este repositorio:

```powershell
dotnet test .\PrismaMusic.Tests\PrismaMusic.Tests.csproj
```

Para comprobar la inicialización de la aplicación empaquetada, ejecútala con `--smoke-test`. La interfaz también admite capturas de prueba de Configuración mediante `PRISMA_SETTINGS_PREVIEW`; ambas opciones son herramientas de validación, no flujos habituales de usuario.
