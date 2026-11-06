# Desarrollo de aplicaciones con .NET 10

[![Build and test](https://github.com/eliaquin/dotnet-development-guide/actions/workflows/ci.yml/badge.svg)](https://github.com/eliaquin/dotnet-development-guide/actions/workflows/ci.yml)

Una API de categorías y productos con arquitectura por capas, Dapper, SQLite y ASP.NET Core Identity. Incluye autenticación por cookies seguras, autorización por roles, protección CSRF y pruebas unitarias e integración.

Consulta la [guía práctica en PDF](docs/guia.pdf), los [diagramas](docs/assets/architecture.png), las [decisiones técnicas](docs/decisiones.md) y la [evidencia de validación](docs/validacion.md).

## Requisitos

- [SDK .NET 10.0.403](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), con runtime 10.0.14, o un parche compatible de la misma banda de SDK.
- Git.
- Python 3 para el cliente de demostración y, opcionalmente, para generar la guía.

SQLite se distribuye con el proveedor NuGet. El proyecto se ejecuta en una carpeta local con permisos de escritura; no necesita un servidor de base de datos.

## Compilar y probar

```sh
git clone https://github.com/eliaquin/dotnet-development-guide.git
cd dotnet-development-guide
dotnet restore --locked-mode
dotnet build Store.slnx --configuration Release --no-restore
dotnet test --solution Store.slnx --configuration Release --no-build
dotnet dev-certs https --trust
```

`global.json` selecciona el SDK y Microsoft.Testing.Platform. `Directory.Packages.props` fija las dependencias directas y cada proyecto incluye su archivo de bloqueo.

## Crear el administrador inicial

El aprovisionamiento solo se ejecuta sobre una base sin usuarios y termina sin iniciar el servidor. Introduce una contraseña de 15 a 128 caracteres; no existe una contraseña predeterminada.

En Bash:

```bash
export Bootstrap__UserName=admin
read -r -s -p 'Contraseña inicial: ' Bootstrap__Password
export Bootstrap__Password
dotnet run --project src/Store.Api --configuration Release --no-build -- --Bootstrap:Enabled=true --environment Development
unset Bootstrap__Password Bootstrap__UserName
```

En PowerShell:

```powershell
$env:Bootstrap__UserName = 'admin'
$secret = Read-Host 'Contraseña inicial' -AsSecureString
$env:Bootstrap__Password = [Net.NetworkCredential]::new('', $secret).Password
dotnet run --project src/Store.Api --configuration Release --no-build -- --Bootstrap:Enabled=true --environment Development
Remove-Item Env:Bootstrap__Password, Env:Bootstrap__UserName
```

La contraseña se recibe desde el entorno y no aparece en los argumentos del proceso. Elimina las variables al finalizar el comando.

## Iniciar y recorrer la API

```sh
dotnet run --project src/Store.Api --configuration Release --no-build -- --urls https://localhost:7080 --environment Development
```

El comando mantiene la API activa. `--` separa las opciones de `dotnet run` de las opciones de la aplicación. La base está en `src/Store.Api/App_Data/store.db`; `Database:Path` permite elegir otra ruta. Las claves de Data Protection se guardan en `keys`, junto a la base. Detén el servidor con `Ctrl+C`.

En otra terminal, exporta el certificado de desarrollo para que el cliente Python pueda validar HTTPS aunque su almacén de confianza sea distinto del del sistema:

```sh
mkdir -p work
dotnet dev-certs https --export-path work/localhost.pem --format PEM --no-password
python3 scripts/demo.py --base-url https://localhost:7080 --user admin --ca-certificate work/localhost.pem
```

En PowerShell usa `New-Item -ItemType Directory -Force work` para crear la carpeta; los comandos `dotnet` y `python3` son equivalentes. El cliente solicita la contraseña, inicia sesión y demuestra operaciones del catálogo. Añade `--create-user lector --role Reader` para crear una cuenta con la sesión administradora. Usa `--user lector --read-only` para consultar con permisos de lectura. Gestiona cookies y tokens CSRF sin poner la contraseña en la línea de comandos. El directorio `work` está excluido de Git; los archivos de certificado y su clave privada son material local de desarrollo.

Comprobaciones sin sesión:

```sh
curl -f https://localhost:7080/health
curl -i https://localhost:7080/api/categories
curl -f https://localhost:7080/openapi/v1.json
```

Los resultados esperados son 200 para health, 401 para el catálogo sin sesión y 200 para OpenAPI en Development.

## Identidad y permisos

| Rol | Permisos |
|---|---|
| `Reader` | Consultar categorías y productos; gestionar su propia sesión. |
| `Editor` | Permisos de lectura y escrituras del catálogo. |
| `Admin` | Permisos de edición y creación de usuarios con roles permitidos. |

No hay registro público. Un administrador crea cuentas con `POST /api/users` y los campos `userName`, `password` y `role`. El perfil devuelto no expone hashes ni marcas internas.

| Endpoint | Acceso y función |
|---|---|
| `GET /api/auth/csrf` | Anónimo; obtiene token y cookie antiforgery. |
| `POST /api/auth/login` | Anónimo con CSRF; valida `userName` y `password`. |
| `GET /api/auth/me` | Autenticado; devuelve el perfil. |
| `POST /api/auth/logout` | Autenticado con CSRF; cierra todas las sesiones propias. |
| `POST /api/auth/change-password` | Autenticado con CSRF; recibe `currentPassword` y `newPassword`, devuelve 204 y cierra sesiones. |
| `POST /api/users` | Admin con CSRF; crea una cuenta con su rol. |

Las escrituras de controladores, incluido el login, exigen `X-CSRF-TOKEN` y la cookie antiforgery. Después del login hay que obtener otro token CSRF, vinculado a la identidad autenticada. El cliente de demostración implementa ese recorrido.

La política de contraseña admite de 15 a 128 caracteres; el mínimo sigue la recomendación de [OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html). La cookie de sesión es `Secure`, `HttpOnly`, `SameSite=Strict` y dura treinta minutos sin renovación deslizante. Logout y cambio de contraseña revocan las sesiones del usuario. La cuenta se bloquea cinco minutos tras cinco fallos. Login y cambio de contraseña comparten un límite de diez solicitudes por minuto y dirección IP; verificar una contraseña actual incorrecta también cuenta para el bloqueo. Los intentos de una cuenta se serializan dentro de la instancia para proteger el contador de fallos.

## Catálogo y organización

La API ofrece `GET/POST /api/categories`, `GET /api/categories/{id}`, `GET/POST /api/gadgets` y `GET/PUT/DELETE /api/gadgets/{id}`. La consulta requiere lectura; las escrituras requieren edición. Los listados admiten `offset`, `limit` de 1 a 100 y, para productos, `categoryId`.

Los precios son DOP con hasta dos decimales, entre 0 y 1 000 000, persistidos como centavos enteros. `PUT` y `DELETE` exigen la versión consultada para evitar sobrescribir una edición simultánea. Los conflictos devuelven 409; una identidad ausente, 401; un permiso insuficiente, 403. Los errores se expresan como `ProblemDetails`.

| Proyecto | Responsabilidad |
|---|---|
| `Store.Domain` | Entidades y reglas del catálogo. |
| `Store.Application` | Casos de uso y contratos de datos. |
| `Store.Infrastructure` | Dapper, SQLite, scripts SQL y almacén de Identity. |
| `Store.Api` | Contratos HTTP, autenticación, autorización y composición. |
| `Store.UnitTests` | Reglas y servicios aislados. |
| `Store.IntegrationTests` | HTTPS, identidad, permisos y SQLite real temporal. |

## Verificar cambios

```sh
bash scripts/verify.sh
```

En PowerShell o CMD ejecuta los comandos individuales, continuando solo si el anterior termina correctamente:

```sh
dotnet restore --locked-mode
dotnet format Store.slnx --verify-no-changes --no-restore
dotnet build Store.slnx --configuration Release --no-restore
dotnet test --solution Store.slnx --configuration Release --no-build
dotnet list Store.slnx package --vulnerable --include-transitive
```

Las pruebas de integración usan sesiones reales y archivos SQLite temporales independientes. GitHub Actions ejecuta los controles en Ubuntu y Windows. El informe NuGet corresponde a las fuentes consultadas en ese momento.

## Operación y límites

La configuración documentada usa HTTPS en localhost. La base, los secretos, las claves y los certificados privados quedan fuera del repositorio. Un despliegue requiere certificados válidos, claves persistentes protegidas, copias verificadas y configuración de red apropiada. Varias instancias necesitan coordinación distribuida de intentos y límites, además de claves compartidas; el bloqueo en memoria del ejemplo se limita a una instancia.

SQLite admite un escritor activo. WAL mejora la convivencia con lectores; las operaciones SQLite son síncronas y tienen un timeout finito ante bloqueos. `NOCASE` en nombres de categoría solo compara sin distinguir mayúsculas ASCII. El alcance no incluye interfaz gráfica, correo de recuperación, multifactor ni federación.

## Generar la guía

El PDF se genera desde `docs/guia.md` y los diagramas de `docs/assets`. Python solo es necesario para el cliente y las herramientas documentales.

```sh
python3 -m venv work/docs-venv
work/docs-venv/bin/python -m pip install -r scripts/requirements-docs.txt
work/docs-venv/bin/python scripts/build_guide.py
```

En Windows utiliza `work\docs-venv\Scripts\python.exe`. Los diagramas SVG y PNG se regeneran con `python scripts/build_diagrams.py`; el PDF incorpora los SVG como gráficos vectoriales. Renderiza el PDF y revisa todas sus páginas después de editarlo; `<!-- pagebreak -->` marca los cortes de diseño.
