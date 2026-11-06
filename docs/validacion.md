# Validación de la edición 3.0

Fecha: 6 de noviembre de 2026. Plataforma local: macOS ARM64, SDK .NET 10.0.403 y runtime Microsoft.NETCore.App/Microsoft.AspNetCore.App 10.0.14. Configuración Release.

## Controles ejecutados

Se ejecutó `bash scripts/verify.sh` con salida 0:

| Control | Resultado |
|---|---|
| Restauración con `--locked-mode` | Aprobada en seis proyectos |
| `dotnet format --verify-no-changes` | Aprobado |
| Compilación Release | 0 advertencias y 0 errores |
| Pruebas unitarias | 55 aprobadas |
| Pruebas de integración | 90 aprobadas |
| Total | 145 aprobadas; 0 fallidas; 0 omitidas |
| Auditoría NuGet directa y transitiva | Sin vulnerabilidades conocidas según las fuentes consultadas |

```text
Test run summary: Passed!
  total: 145
  failed: 0
  succeeded: 145
  skipped: 0
```

Las pruebas usan Identity, cookies HTTPS y antiforgery reales. La autorización permanece activa al probar el catálogo. Cada escenario tiene su propia base SQLite temporal.

## Escenarios de identidad y autorización

- Acceso anónimo: 401, sin redirección a páginas HTML.
- Reader puede consultar y recibe 403 al escribir o crear cuentas.
- Editor puede modificar el catálogo y recibe 403 al administrar cuentas.
- Admin crea usuarios únicamente con roles permitidos.
- Contraseña incorrecta, cuenta desconocida y cuenta bloqueada devuelven respuestas genéricas.
- Cinco intentos incorrectos, incluidos intentos concurrentes con diferencias de mayúsculas, bloquean la cuenta.
- La contraseña actual incorrecta al cambiarla cuenta para el mismo bloqueo.
- Límite de solicitudes: 429; las cookies alteradas o caducadas no autentican.
- CSRF ausente o incorrecto: 400; el token anónimo no se reutiliza después del login.
- Logout y cambio de contraseña invalidan otras sesiones; la validación observa roles persistidos vigentes.
- Contraseñas de 14 y 129 caracteres se rechazan; 15 caracteres se aceptan sin imponer composición.
- Las contraseñas se persisten como hashes; nunca se devuelven en perfiles.
- Las migraciones conservan datos del catálogo; las actualizaciones de usuarios y roles son atómicas y detectan concurrencia obsoleta.

## Prueba con Kestrel real

Se exportó el certificado de desarrollo a una carpeta temporal y se configuró el cliente Python con ese PEM. No se desactivó la validación TLS ni se modificó la confianza global para ejecutar la prueba.

1. El bootstrap creó el administrador y terminó con código 0, sin iniciar servidor. Repetirlo devolvió 1 sin cambiar la cuenta.
2. HTTPS, `ApiClient` y el recorrido CRUD del cliente de demostración funcionaron con cookies y CSRF.
3. Reader obtuvo 200 al consultar y 403 al escribir; Editor obtuvo 201 al escribir y 403 al crear cuentas.
4. Una solicitud anónima obtuvo 401 y una escritura sin CSRF obtuvo 400.
5. Tras detener y reiniciar Kestrel, una cookie seguía siendo válida y el producto conservaba el precio exacto 1260.75.
6. Logout invalidó una copia de la cookie emitida: la siguiente consulta obtuvo 401.
7. En Production, health respondió 200. OpenAPI no está publicado: una sesión válida obtuvo 404; un anónimo obtuvo 401 por la política predeterminada.

Los procesos finalizaron y el puerto 7080 quedó libre. Credenciales aleatorias, bases, claves y certificados temporales se mantuvieron fuera del repositorio.

## Documento y diagramas

La guía consta de 21 páginas y se genera desde `docs/guia.md`. Incluye dos diagramas vectoriales, con fuentes SVG y vistas PNG en `docs/assets`. Se comprobó la coherencia del texto con la implementación y sus comandos. Se renderizó el PDF para revisar todas sus páginas.

## Integración continua

El [workflow público](https://github.com/eliaquin/dotnet-development-guide/actions/workflows/ci.yml) repite restauración bloqueada, formato, compilación, pruebas y auditoría en Ubuntu y Windows. La [ejecución 42222043139](https://github.com/eliaquin/dotnet-development-guide/actions/runs/42222043139) valida el commit `9cb15609aaea38df9aa83b6cc70d7c10c318be68`. Sus logs conservan los resultados de cada plataforma.

## Alcance

La aplicación está configurada para una instancia local HTTPS. Cuenta con usuarios, roles, bloqueo, cookies y CSRF. No incluye interfaz gráfica, recuperación por correo, MFA ni federación. Varias instancias requieren coordinación de intentos y rate limiting, claves compartidas protegidas y una base apropiada para la carga.
