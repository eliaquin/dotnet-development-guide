# Decisiones técnicas

Fecha de referencia: 6 de noviembre de 2026. La solución es una API de catálogo con cuentas locales, autorización por roles y persistencia SQLite. Las decisiones se orientan a una ejecución reproducible y a responsabilidades que puedan verificarse mediante pruebas.

## Arquitectura

| Área | Decisión | Razón |
|---|---|---|
| Dominio | Reglas puras y entidades del catálogo. | Las invariantes se pueden probar sin HTTP ni base de datos. |
| Aplicación | Casos de uso y contrato `IStoreRepository`. | La coordinación no depende del lenguaje SQL ni del proveedor. |
| Infraestructura | Dapper, conexiones, migraciones y almacén de Identity. | El acceso persistente tiene una frontera explícita. |
| API | DTO, controladores, políticas y composición. | Los contratos HTTP y la seguridad de las solicitudes quedan visibles. |
| Dependencias | Inyección por constructor y registros explícitos. | Facilita entender los ciclos de vida y construir pruebas aisladas. |
| Mapeo | Conversión explícita entre entidades y respuestas. | Hace revisables los campos expuestos y los que puede escribir el cliente. |

Los proyectos de dominio y aplicación no conocen el ticket de autenticación ni las interfaces de ASP.NET Core. La API aplica las políticas antes de ejecutar los casos de uso. La infraestructura implementa los contratos del catálogo y los de almacenamiento de identidad.

## Plataforma y reproducción

El objetivo es `net10.0` con SDK 10.0.403 y runtime 10.0.14. `global.json` permite parches posteriores de esa banda y configura Microsoft.Testing.Platform. Las versiones de paquetes están en `Directory.Packages.props`; los archivos `packages.lock.json` hacen explícita la resolución de dependencias.

Dapper 2.1.89 ejecuta consultas parametrizadas. Microsoft.Data.Sqlite 10.0.14 proporciona una base local distribuida con NuGet. Las pruebas usan xUnit.net v3, paquete `xunit.v3.mtp-v2` 4.0.2, y Microsoft.AspNetCore.Mvc.Testing 10.0.14. Los proyectos de pruebas son ejecutables compatibles con el runner del SDK.

La configuración compartida habilita referencias anulables, analizadores, compilación determinista y advertencias como errores. `scripts/verify.sh` repite restauración bloqueada, formato, compilación, pruebas y auditoría. Los parches se actualizan deliberadamente, revisando notas y regenerando los bloqueos antes de validar el conjunto.

Fuentes: [soporte .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [metadata .NET 10](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), [Dapper](https://github.com/DapperLib/Dapper), [xUnit y MTP](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform).

## Identidad y sesiones

ASP.NET Core Identity administra nombres normalizados, contraseñas y bloqueo. `DapperUserStore` implementa sus contratos sobre las tablas `Users` y `UserRoles`. `UserManager` y `SignInManager` ejecutan las operaciones de cuenta; la aplicación no implementa su propio formato de contraseña ni su propio cifrado de tickets.

`PasswordHasher` utiliza el formato Identity V3, PBKDF2-HMAC-SHA512, sal aleatoria y 210 000 iteraciones. La política admite de 15 a 128 caracteres, incluidos espacios, sin imponer una composición artificial de símbolos. El mínimo corresponde a la recomendación OWASP para un acceso sin multifactor. La contraseña nunca se recorta ni se registra. Cinco intentos fallidos bloquean la cuenta durante cinco minutos. Login y verificación de contraseña actual en el cambio de contraseña cuentan para el bloqueo y comparten un límite de diez solicitudes por minuto y dirección IP. Los intentos de una cuenta se serializan dentro de una instancia para proteger el contador ante escrituras simultáneas.

La cookie `__Host-Store.Auth` usa `Secure`, `HttpOnly`, `SameSite=Strict` y una duración fija de treinta minutos, sin renovación deslizante. El prefijo exige `Secure`, ruta `/` y ausencia de `Domain`. Data Protection protege el ticket y guarda sus claves en un directorio persistente junto a la base.

La validación de la sesión comprueba el estado del usuario y su `SecurityStamp`. Logout invalida todas las cookies emitidas para el usuario y elimina la cookie del cliente. El cambio de contraseña también exige iniciar sesión de nuevo. `ConcurrencyStamp` tiene otra responsabilidad: detectar una actualización obsoleta de la fila de usuario.

El aprovisionamiento inicial se realiza desde la terminal con `Bootstrap:Enabled=true` y secretos recibidos por entorno. Solo se ejecuta sobre una base sin usuarios y termina sin abrir un servidor. No hay contraseña predeterminada ni registro público. Las cuentas adicionales se crean por un administrador autenticado. El alcance no incluye correo de recuperación, multifactor ni federación.

Fuentes: [almacenes de Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-custom-storage-providers?view=aspnetcore-10.0), [configuración de Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0), [Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0), [autenticación OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html) y [almacenamiento de contraseñas OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).

## Autorización y CSRF

La política por defecto exige una identidad autenticada. Los permisos de lectura admiten `Reader`, `Editor` o `Admin`; los de escritura admiten `Editor` o `Admin`; la creación de cuentas requiere `Admin`. Los permisos se evalúan en el servidor, con roles controlados y datos persistidos.

Health, login, la obtención del token CSRF y OpenAPI en Development tienen acceso anónimo explícito. Health solo devuelve un estado de disponibilidad. El catálogo requiere autenticación. Una identidad ausente produce `401`; una identidad sin el permiso requerido produce `403`. Los endpoints de API no redirigen esas respuestas a páginas de acceso.

Las escrituras de los controladores usan antiforgery, incluido el login. El cliente obtiene cookie y token mediante `GET /api/auth/csrf` y envía `X-CSRF-TOKEN` en cada operación insegura. Después del login obtiene otro token porque la identidad asociada cambió. `SameSite=Strict` se combina con esta protección y no se configura CORS abierto con credenciales.

Fuentes: [políticas](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0), [autenticación de endpoints API](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/api-endpoint-auth?view=aspnetcore-10.0), [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0).

## Datos y concurrencia

Las consultas nombran sus columnas y usan parámetros para valores. Los únicos fragmentos SQL interpolados son listas de columnas constantes. Los listados tienen orden por Id, filtro opcional y límites de página entre 1 y 100.

Cada operación abre una conexión con claves foráneas activas, timeout de cinco segundos y pooling deshabilitado. El proveedor SQLite ejecuta sus métodos ADO.NET async de forma síncrona. El catálogo usa llamadas síncronas; el almacén cumple las firmas `Task` de Identity y comprueba cancelación antes de las operaciones, sin prometer interrupción inmediata de una instrucción nativa.

El precio representa DOP con dos decimales, de 0 a 1 000 000. Se guarda como centavos `INTEGER`, para aritmética exacta. Las fechas se almacenan en UTC como texto roundtrip y se convierten explícitamente. La unicidad `NOCASE` de categorías solo ignora diferencias de mayúsculas ASCII; una regla lingüística distinta requiere normalización e intercalación apropiadas.

WAL mejora la convivencia entre lectores y escritor, pero SQLite admite un escritor activo. Las escrituras del catálogo incluyen versión esperada. Una actualización incrementa la versión atómicamente; una escritura obsoleta devuelve conflicto sin cambiar datos. Las escrituras de usuario y roles se confirman en una misma transacción y usan la marca de concurrencia de Identity.

Los scripts SQL numerados se aplican de forma secuencial dentro de una transacción y `PRAGMA user_version` registra el esquema. Se rechaza una versión superior a la conocida. Las restricciones `STRICT`, `CHECK`, `UNIQUE` y las claves foráneas protegen la integridad aunque se intente escribir fuera del dominio.

Fuentes: [limitaciones Dapper/SQLite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/dapper-limitations), [E/S SQLite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async), [transacciones](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions), [intercalaciones](https://www.sqlite.org/datatype3.html#collating_sequences).

## Operación y verificación

La ejecución documentada usa HTTPS en localhost. La base, las claves, los certificados privados y los secretos quedan fuera del repositorio. Una operación compartida requiere certificados válidos, claves protegidas y persistentes, copias de respaldo con restauración comprobada y una topología adecuada a la carga. El bloqueo de intentos en memoria y el limitador de solicitudes tienen alcance de instancia; el escalado horizontal exige coordinación distribuida de ambos y un almacén de claves compartido.

Las pruebas de integración se autentican mediante endpoints reales y usan SQLite temporal. Verifican permisos, CSRF, sesiones y efectos de datos, además del CRUD. GitHub Actions ejecuta los controles en Ubuntu y Windows. [Validación registrada](validacion.md).
