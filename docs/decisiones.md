# Decisiones técnicas

Edición verificada el 6 de noviembre de 2026. La guía conserva las responsabilidades de presentación, servicios, modelos y acceso a datos. Su implementación actual usa una API de catálogo pequeña y ejecutable sin un servidor de base de datos.

## Qué se conserva y qué cambia

| Tema | Decisión | Motivo y alternativa |
|---|---|---|
| Separación de responsabilidades | Conservar modelos de dominio, aplicación, infraestructura y presentación. | Cada proyecto tiene una responsabilidad verificable; crear carpetas por sí solo no garantiza separación. |
| Acceso a datos | Conservar el acceso exclusivo desde infraestructura y definir `IStoreRepository` en aplicación. | Los servicios no conocen Dapper ni SQLite. Los controladores llaman casos de uso, sin ejecutar SQL. |
| Plataforma | Sustituir .NET Core 2.1 por .NET 10 LTS; base probada SDK 10.0.403/runtime 10.0.14. | Usar una plataforma soportada y parches verificables. El [ciclo de soporte oficial](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) permite revisar la vigencia. |
| Entity Framework Core | Sustituir por Dapper 2.1.89. | Es la elección de acceso SQL de esta guía. Dapper mapea consultas a objetos; no ofrece seguimiento de entidades, `DbContext`, `SaveChanges` ni migraciones. [Dapper](https://github.com/DapperLib/Dapper). |
| Proveedor SQL Server | Sustituir por Microsoft.Data.Sqlite 10.0.14 para el ejemplo. | Un archivo local permite ejecutar y probar sin instalar un servidor. Un sistema con otras necesidades puede usar SQL Server o PostgreSQL, adaptando SQL, proveedor y pruebas de integración. |
| Configuración Code First y Fluent API | Sustituir por una migración SQL embebida y versionada. | Las restricciones y tipos quedan explícitos. La evolución del esquema es independiente del mapeo de objetos. |
| Factoría de `DbContext` | Sustituir por `SqliteConnectionFactory`. | Cada operación abre y desecha su propia conexión; no se comparte una conexión mutable entre solicitudes. |
| `Create` seguido de `Save` desde presentación | Sustituir por una operación de servicio que persiste y devuelve el resultado. | La aplicación expone un caso de uso completo. Una secuencia que requiera atomicidad debe compartir conexión y transacción. |
| Autofac y su extensión de DI | Omitir; usar el contenedor incluido y registros explícitos. | La solución no necesita características adicionales de un contenedor externo. Autofac sigue siendo una alternativa cuando requisitos concretos lo justifican. [DI de ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-10.0). |
| Registro por convención de nombres | Sustituir por registro explícito en `Program.cs`. | Las dependencias de un ejemplo pequeño quedan visibles sin escaneo de ensamblados. |
| AutoMapper y su extensión de DI | Omitir; usar mapeo explícito. | Pocos campos no justifican otra dependencia. No se afirma que AutoMapper sea inadecuado para todos los proyectos. |
| ViewModels y validación | Conservar contratos de entrada propios, DataAnnotations y reglas de dominio. | `[ApiController]` valida el contrato HTTP; el dominio protege invariantes aun cuando el caso de uso se invoque sin HTTP. La base agrega `CHECK`, `UNIQUE` y claves foráneas. |
| MVC con vistas | Adaptar a API con controladores y `[ApiController]`. | Se conservan controladores, servicios y separación lógica. Se omiten Razor, formularios y CSS porque esta demo se prueba mediante HTTP y no tiene interfaz gráfica. |
| Identity con almacenes de Entity Framework | Omitir del catálogo local. | El ejemplo no administra cuentas. Una ampliación debe elegir un proveedor de identidad mantenido y aplicar autenticación y autorización sin reintroducir EF por accidente. |
| Configuración `Startup` y compatibilidad 2.1 | Sustituir por hosting y composición en `Program.cs`. | La configuración se mantiene en el punto de entrada actual de ASP.NET Core. |
| Serialización, logging y configuración | Usar System.Text.Json, ILogger y configuración incluidos. | No hacen falta bibliotecas adicionales para las necesidades del ejemplo. |
| Documentación HTTP | Usar Microsoft.AspNetCore.OpenApi 10.0.14, habilitado en desarrollo. | Genera `/openapi/v1.json`; no requiere una interfaz Swagger adicional. [OpenAPI en ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0). |
| Pruebas automatizadas | Agregar xUnit.net con `xunit.v3.mtp-v2` 4.0.2 y Microsoft.Testing.Platform. | Biblioteca mantenida y runner integrado con .NET 10. MSTest y NUnit son alternativas válidas; no existe una biblioteca única obligatoria para todo .NET. [xUnit y MTP](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform). |
| Integración HTTP | Usar Microsoft.AspNetCore.Mvc.Testing 10.0.14 y SQLite real temporal. | Comprueba contratos, persistencia y restricciones reales sin sustituir consultas por mocks. |
| Tiempo | Inyectar `TimeProvider` y guardar UTC explícito. | Los servicios pueden probarse con un reloj controlado. La conversión a la zona del usuario corresponde a presentación. |

## Dependencias y reproducción

Las versiones están centralizadas en `Directory.Packages.props`; cada proyecto incluye `packages.lock.json`. `global.json` selecciona SDK 10.0.403, admite los parches posteriores de esa banda y configura Microsoft.Testing.Platform. Los proyectos de pruebas son ejecutables y no necesitan el adaptador de VSTest para este flujo.

El código habilita referencias anulables, analizadores, compilación determinista y advertencias como errores. `scripts/verify.sh` comprueba formato, compilación, pruebas y el informe de vulnerabilidades. Un informe limpio describe las fuentes consultadas en ese momento: conviene repetirlo al actualizar dependencias.

Las versiones son una base reproducible, no una instrucción para congelar parches de seguridad. Para actualizar, revisa las notas del proveedor, cambia la versión central, restaura para regenerar bloqueos, ejecuta la verificación completa y revisa el diff. Las fechas y versiones oficiales pueden consultarse en los [metadatos de .NET 10](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json), [NuGet Dapper](https://www.nuget.org/packages/Dapper/2.1.89) y [NuGet xUnit](https://www.nuget.org/packages/xunit.v3.mtp-v2/4.0.2).

## Dapper y límites de SQLite

Las consultas nombran sus columnas y envían datos mediante parámetros. Los únicos fragmentos SQL interpolados son listas de columnas constantes definidas en el código; los valores recibidos por HTTP nunca se concatenan. Los listados tienen orden estable y paginación limitada.

El proveedor Microsoft.Data.Sqlite ejecuta sus métodos ADO.NET asíncronos de forma síncrona. Por eso este repositorio usa métodos síncronos de Dapper y no simula asincronía con `Task.Run`. El timeout de cinco segundos limita la espera por un bloqueo; no se promete cancelar inmediatamente una instrucción nativa cuando se desconecta un cliente. Para otro proveedor, revisa su soporte real de E/S asíncrona y cancelación. [Limitaciones async](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async).

WAL mejora la coexistencia de lectores y escritor, pero SQLite permite un único escritor con cambios pendientes. Cada conexión habilita claves foráneas y usa un timeout finito. Las conexiones se desechan por operación y el pooling está deshabilitado para simplificar el ciclo de vida local. Si la carga exige muchas escrituras concurrentes, mide y considera un motor de servidor. [Transacciones SQLite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions).

El precio representa pesos dominicanos (DOP) con dos decimales. El dominio acepta valores de 0 a 1,000,000 y rechaza precisión adicional; infraestructura guarda centavos como `INTEGER`, nunca como `REAL`. Las fechas se guardan como texto UTC en formato roundtrip `O`, con conversión explícita a `DateTimeOffset`. Esto evita depender de conversiones que Dapper y el proveedor no realizan automáticamente. [Limitaciones de Dapper con SQLite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/dapper-limitations).

La unicidad del nombre de categoría usa la intercalación `NOCASE` incluida en SQLite. Su comparación sin distinción de mayúsculas abarca ASCII; no supone equivalencia entre todas las letras Unicode ni elimina acentos. Si el negocio exige otras reglas, define normalización e intercalación apropiadas, una migración y pruebas para sus casos concretos. [Intercalaciones de SQLite](https://www.sqlite.org/datatype3.html#collating_sequences).

## Esquema y concurrencia

`DatabaseInitializer.Initialize()` crea el directorio, activa WAL y aplica `Migrations/001_initial.sql` dentro de una transacción. `PRAGMA user_version` registra la versión 1. Iniciar de nuevo no vuelve a aplicar la migración; una versión de base superior a la conocida provoca un error en lugar de continuar con un esquema desconocido. La base inicial no contiene datos de demostración.

Las tablas `STRICT` añaden tipos y restricciones explícitas: nombre requerido de hasta 50 caracteres, descripción de hasta 500, precio en el rango permitido, versión positiva y categoría existente. `AUTOINCREMENT` evita reutilizar identificadores eliminados y confundir un cliente con datos antiguos con un registro nuevo.

La versión del producto permite concurrencia optimista. `UPDATE` y `DELETE` incluyen `Id` y `Version` en el `WHERE`. La actualización incrementa la versión en la misma instrucción. Si ninguna fila coincide, una consulta dentro de la misma transacción distingue ausencia (`404`) de versión obsoleta (`409`). Las restricciones únicas y de clave foránea resuelven también conflictos que aparezcan entre la validación de aplicación y la escritura.

La inicialización incluida resuelve una base local nueva; no constituye un sistema completo de migraciones de producción. Para una versión 2, agrega una migración numerada sin modificar la ya aplicada, selecciona cada paso según la versión existente y actualiza `user_version` únicamente después de ejecutarlo correctamente en la misma transacción. Prueba la actualización desde una copia de la versión anterior y verifica preservación de datos, restricciones y recuperación. En bases compartidas, realiza copias de respaldo verificadas y aplica los cambios mediante un proceso controlado antes de desplegar las nuevas instancias. [PRAGMA user_version](https://www.sqlite.org/pragma.html#pragma_user_version).

## Alcance de seguridad y operación

La ejecución documentada escucha en `127.0.0.1` y no incluye usuarios, contraseñas ni tokens. Su finalidad es enseñar un flujo local reproducible. Para publicar una aplicación derivada, incorpora autenticación y autorización según los recursos y actores reales, HTTPS, secretos fuera del repositorio, límites operativos, registros y copias de respaldo con restauración comprobada. Estas decisiones deben estar completas antes de cambiar la exposición de red.

Mantén las responsabilidades al ampliar el sistema: contrato HTTP en API, reglas y coordinación en aplicación/dominio, SQL y proveedores en infraestructura, y pruebas en el nivel que pueda detectar el fallo real. Añade abstracciones cuando resuelvan una necesidad concreta; el ejemplo no requiere un repositorio genérico, un bus de mensajes ni un contenedor externo.
