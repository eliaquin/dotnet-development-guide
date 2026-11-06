# Guía de desarrollo con .NET 10

Un catálogo local de categorías y productos que muestra separación de responsabilidades, SQL parametrizado con Dapper, reglas de dominio y pruebas automatizadas. La presentación es una API HTTP con controladores; no incluye interfaz gráfica ni autenticación.

Consulta la [guía de implementación en PDF](docs/guia.pdf) y las [decisiones técnicas](docs/decisiones.md).

## Requisitos

- [SDK .NET 10.0.403](https://dotnet.microsoft.com/download/dotnet/10.0), que incluye el runtime 10.0.14, o un parche compatible de la misma banda de SDK. `global.json` fija esta base y admite sus parches posteriores.
- Git. No se necesita instalar SQLite ni iniciar un servidor de base de datos.

Esta edición se verificó el 6 de noviembre de 2026. .NET 10 es la versión LTS utilizada; conserva las actualizaciones de seguridad y revisa sus dependencias antes de actualizar los archivos de bloqueo.

## Ejecutar desde cero

Desde una terminal con `dotnet` en el `PATH`:

```sh
git clone https://github.com/eliaquin/dotnet-development-guide.git
cd dotnet-development-guide
dotnet --version
dotnet restore --locked-mode
dotnet build Store.slnx --configuration Release --no-restore
dotnet test --solution Store.slnx --configuration Release --no-build
dotnet run --project src/Store.Api --configuration Release --no-build -- --urls http://127.0.0.1:5080 --environment Development
```

El último comando mantiene la API activa. `--` separa las opciones de `dotnet run` de los argumentos de la aplicación. El entorno `Development` habilita el documento OpenAPI en [localhost:5080/openapi/v1.json](http://127.0.0.1:5080/openapi/v1.json).

La base se crea sin datos iniciales en `src/Store.Api/App_Data/store.db`. `Database:Path` permite configurar otra ruta; las rutas relativas se resuelven desde el directorio de contenido de la API. La creación del esquema es automática y transaccional. Detén la API con `Ctrl+C`.

## Probar la API

En otra terminal con Bash o una shell compatible:

```sh
curl --fail-with-body http://127.0.0.1:5080/health

curl --fail-with-body -X POST http://127.0.0.1:5080/api/categories \
  -H 'Content-Type: application/json' \
  -d '{"name":"Accesorios"}'

curl --fail-with-body -X POST http://127.0.0.1:5080/api/gadgets \
  -H 'Content-Type: application/json' \
  -d '{"name":"Teclado","description":"Teclado USB","price":1250.50,"categoryId":1}'

curl --fail-with-body 'http://127.0.0.1:5080/api/gadgets?offset=0&limit=50'
```

`categoryId: 1` supone una base nueva. Usa el `id` devuelto al crear la categoría si ya contiene datos. Repetir el mismo nombre de categoría produce `409 Conflict`.

Los precios del ejemplo se expresan en pesos dominicanos (DOP), con hasta dos decimales, entre 0 y 1,000,000. Se almacenan como centavos enteros para conservar exactitud. No hay conversión de monedas.

En PowerShell, los comandos `dotnet` anteriores son iguales. Para probar la API sin depender de las reglas de comillas de `curl`:

```powershell
$base = 'http://127.0.0.1:5080'
Invoke-RestMethod "$base/health"
$category = Invoke-RestMethod "$base/api/categories" -Method Post -ContentType 'application/json' -Body (@{name='Perifericos'} | ConvertTo-Json)
$gadget = @{name='Raton'; description='Raton USB'; price=450.50; categoryId=$category.id}
Invoke-RestMethod "$base/api/gadgets" -Method Post -ContentType 'application/json' -Body ($gadget | ConvertTo-Json)
Invoke-RestMethod "$base/api/gadgets?offset=0&limit=50"
```

## Estructura y comportamiento

| Proyecto | Responsabilidad |
|---|---|
| `src/Store.Domain` | Modelos y reglas sin dependencias de persistencia o HTTP. |
| `src/Store.Application` | Casos de uso, contrato de repositorio y reloj inyectable. |
| `src/Store.Infrastructure` | Dapper, conexiones SQLite y migración SQL embebida. |
| `src/Store.Api` | Contratos HTTP, validación, controladores, errores y composición de dependencias. |
| `tests/Store.UnitTests` | Reglas y servicios aislados con xUnit.net. |
| `tests/Store.IntegrationTests` | API HTTP y SQLite real en archivos temporales. |

La API ofrece `GET/POST /api/categories`, `GET /api/categories/{id}`, `GET/POST /api/gadgets` y `GET/PUT/DELETE /api/gadgets/{id}`. Los listados usan `offset` y `limit` (predeterminado 50; máximo 100). Productos admite el filtro `categoryId`.

`PUT` exige el `version` obtenido al consultar el producto; `DELETE` lo recibe en `?version=...`. Una actualización exitosa incrementa la versión. Un dato inválido devuelve `400`, un recurso ausente `404` y una versión obsoleta o nombre de categoría duplicado `409`. Los errores se devuelven como `ProblemDetails`.

## Verificar cambios

En macOS/Linux o Git Bash:

```sh
bash scripts/verify.sh
```

El script restaura con bloqueo, comprueba formato, compila con advertencias como errores, ejecuta ambas suites y consulta vulnerabilidades directas y transitivas. La consulta de vulnerabilidades informa sobre el estado disponible en NuGet; revisa su resultado.

Equivalente en PowerShell o CMD, ejecutando cada comando solo si terminó correctamente el anterior:

```sh
dotnet restore --locked-mode
dotnet format Store.slnx --verify-no-changes --no-restore
dotnet build Store.slnx --configuration Release --no-restore
dotnet test --solution Store.slnx --configuration Release --no-build
dotnet list Store.slnx package --vulnerable --include-transitive
```

## Alcance del ejemplo

El comando de inicio escucha solo en loopback para desarrollo local. Antes de exponer una aplicación derivada, define autenticación y autorización según sus casos de uso, HTTPS, gestión de secretos, copias de respaldo y operación. La demo no implementa cuentas ni contraseñas y no depende de Entity Framework.

SQLite usa un único escritor a la vez. WAL facilita la convivencia entre lecturas y escrituras, con un timeout de cinco segundos ante bloqueos; las operaciones del repositorio son síncronas y no prometen cancelación inmediata. La unicidad `NOCASE` de nombres de categoría solo compara sin distinguir mayúsculas para caracteres ASCII. Consulta los límites y la evolución del esquema en las [decisiones técnicas](docs/decisiones.md).

## Regenerar la guía

El PDF se genera desde `docs/guia.md`. La edición actual contiene 16 páginas revisadas visualmente. Python solo es necesario para regenerar el documento, no para ejecutar la aplicación.

```sh
python3 -m venv work/docs-venv
work/docs-venv/bin/python -m pip install -r scripts/requirements-docs.txt
work/docs-venv/bin/python scripts/build_guide.py
```

En Windows usa `work\docs-venv\Scripts\python.exe`. Después de editar, renderiza el PDF y revisa todas las páginas; el corte intencional `<!-- pagebreak -->` mantiene la organización.

La [evidencia de validación](docs/validacion.md) registra las versiones y resultados de esta edición.
