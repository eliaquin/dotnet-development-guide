# Validación de la edición 2.0

Fecha: 6 de noviembre de 2026. Código y guía comprobados en macOS ARM64 con SDK .NET 10.0.403, runtime Microsoft.NETCore.App/Microsoft.AspNetCore.App 10.0.14 y configuración Release.

## Verificación local

Se ejecutó `bash scripts/verify.sh` desde la raíz, con salida 0:

| Control | Resultado |
|---|---|
| `dotnet restore --locked-mode` | Aprobado en los seis proyectos |
| `dotnet format Store.slnx --verify-no-changes --no-restore` | Aprobado |
| `dotnet build Store.slnx --configuration Release --no-restore` | Aprobado; 0 advertencias, 0 errores |
| `dotnet test --solution Store.slnx --configuration Release --no-build` | 102 aprobadas; 0 fallidas; 0 omitidas |
| Suite unitaria | 55 casos de dominio y aplicación |
| Suite de integración | 47 casos HTTP y SQLite real |
| `dotnet list Store.slnx package --vulnerable --include-transitive` | Sin paquetes vulnerables conocidos según las fuentes NuGet consultadas |

Resumen de la salida del runner:

```text
Test run summary: Passed!
  total: 102
  failed: 0
  succeeded: 102
  skipped: 0
```

Los casos de integración incluyen campos obligatorios, normalización antes de validar longitudes, dinero exacto, SQL tratado como dato, restricciones FK/CHECK, migración repetible, rechazo de un esquema futuro sin pérdida de datos, escritura concurrente 200/409 y errores 500 que registran la excepción sin divulgarla al cliente.

## Prueba con servidor real

Se inició `dotnet run --project src/Store.Api --configuration Release --no-build -- --urls http://127.0.0.1:5080 --environment Development` con `--Database:Path` dirigido a una carpeta temporal. Las solicitudes se enviaron por HTTP a Kestrel, fuera del host de pruebas:

- `/health`: 200; `/openapi/v1.json`: 200 en Development.
- Categoría y producto: 201, `Location` y fecha UTC válidos.
- Duplicado: 409; entrada inválida: 400.
- Consulta y listado: 200; edición: precio exacto 1260.75, versión 2.
- Edición y borrado con versión anterior: 409.
- Reinicio del host: categoría y producto conservados, mismo precio y versión.
- Borrado con versión actual: 204; consulta posterior: 404; listado vacío.
- Inicio en Production: OpenAPI 404; health y categoría persistida 200.

Se detuvieron los procesos al finalizar. La base temporal no forma parte del repositorio.

## Integración continua

El [workflow de GitHub Actions](https://github.com/eliaquin/dotnet-development-guide/actions/workflows/ci.yml) ejecuta la misma secuencia en Ubuntu y Windows. El estado remoto de la edición se registra aquí tras su publicación.

## Documento

`docs/guia.pdf` se genera desde `docs/guia.md` con ReportLab 4.4.9. Se renderizaron y revisaron visualmente sus 16 páginas, además de extraer su texto para verificar contenido, referencias y ausencia de menciones institucionales.

La validación describe los escenarios comprobados y las versiones indicadas. No incluye una prueba de carga ni una auditoría de seguridad externa. La demo escucha en loopback y no implementa autenticación.
