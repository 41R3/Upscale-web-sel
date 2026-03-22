# Upscale-web

Aplicación web ASP.NET Core MVC para gestión de usuarios con inicio de sesión, sesión por inactividad, bloqueo temporal por intentos fallidos y vista de perfil.

## 1) Resumen funcional

El proyecto implementa:

- Pantalla pública de bienvenida (`/Home/Index`) con acceso a login.
- Autenticación por documento (`DNI` o `CE`) y contraseña.
- Control de cuenta activa/inactiva (`EstadoActivo`).
- Bloqueo temporal de 15 minutos tras 5 intentos fallidos.
- Pantalla de cuenta bloqueada con contador regresivo y redirección automática a login.
- Sesión web con expiración por inactividad (20 minutos) y modal de advertencia antes de expirar.
- Perfil de usuario con datos personales/laborales.
- Envío opcional de correo cuando una cuenta queda bloqueada (si SMTP está configurado).

## 2) Stack y tecnologías

- **Backend**: ASP.NET Core MVC (.NET 10)
- **ORM/DB**: Entity Framework Core 10 + SQL Server
- **Frontend**: Razor Views, Bootstrap 5, Bootstrap Icons, JavaScript
- **Estado de sesión**: `AddSession` con cookie y timeout por inactividad

Dependencias principales en `Upscale-web/Upscale-web.csproj`:

- `Microsoft.EntityFrameworkCore` 10.0.0
- `Microsoft.EntityFrameworkCore.SqlServer` 10.0.0

## 3) Requisitos previos

- SDK de .NET 10 instalado (el proyecto apunta a `net10.0`).
- SQL Server accesible (local o remoto).
- Credenciales válidas en cadena de conexión.
- (Opcional) Servidor SMTP si se desea enviar correos de bloqueo.

## 4) Configuración

### 4.1 Cadena de conexión

Archivo: `Upscale-web/appsettings.json`

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost,1433;Database=SistemaGestion;User Id=sa;Password=...;TrustServerCertificate=True;"
}
```

### 4.2 SMTP (opcional)

Archivo: `Upscale-web/appsettings.json`

```json
"Smtp": {
  "Host": "",
  "Port": 587,
  "EnableSsl": true,
  "UserName": "",
  "Password": "",
  "From": "",
  "FromName": "Sistema de Gestión"
}
```

Si `Host`, `From` o `usuario.Email` no están definidos, el sistema no envía correo y solo registra advertencia en logs.

### 4.3 Perfiles de ejecución

Archivo: `Upscale-web/Properties/launchSettings.json`

- `http`: `http://localhost:5131`
- `https`: `https://localhost:7125` y `http://localhost:5131`

## 5) Ejecución local

Desde la raíz del workspace:

```bash
cd /home/sel/RiderProjects/Upscale-web
dotnet restore Upscale-web.sln
dotnet run --project Upscale-web/Upscale-web.csproj
```

Al iniciar:

- Se crea la base si no existe (`EnsureCreated`).
- Se ejecuta una sincronización básica de columnas en `dbo.Usuarios` mediante SQL crudo (`Program.cs`).

## 6) Estructura del proyecto

```text
Upscale-web/
├─ Upscale-web.sln
└─ Upscale-web/
   ├─ Controllers/
   │  ├─ AccountController.cs
   │  └─ HomeController.cs
   ├─ Data/
   │  └─ ApplicationDbContext.cs
   ├─ Models/
   │  ├─ Usuario.cs
   │  └─ ErrorViewModel.cs
   ├─ Views/
   │  ├─ Account/
   │  │  ├─ Login.cshtml
   │  │  ├─ Bloqueada.cshtml
   │  │  └─ Perfil.cshtml
   │  ├─ Home/
   │  │  ├─ Index.cshtml
   │  │  └─ Privacy.cshtml
   │  └─ Shared/
   │     ├─ _Layout.cshtml
   │     └─ Error.cshtml
   ├─ wwwroot/
   │  ├─ css/site.css
   │  ├─ js/site.js
   │  └─ lib/...
   ├─ Program.cs
   ├─ appsettings.json
   └─ appsettings.Development.json
```

## 7) Archivos principales y responsabilidad

- `Upscale-web/Program.cs`
  - Configura DI, EF Core, sesión (`IdleTimeout = 20 min`), pipeline MVC y rutas.
  - Ejecuta inicialización/sincronización de esquema de `Usuarios`.

- `Upscale-web/Data/ApplicationDbContext.cs`
  - Define el `DbContext` y `DbSet<Usuario> Usuarios`.

- `Upscale-web/Models/Usuario.cs`
  - Entidad principal de usuario con datos personales, laborales y campos de seguridad:
    - `EstadoActivo`
    - `IntentosFallidos`
    - `BloqueadoHasta`
  - Propiedades calculadas (`NotMapped`) para nombre/documento completo.

- `Upscale-web/Controllers/AccountController.cs`
  - Login, perfil, keep-alive de sesión, logout y pantalla de bloqueo.
  - Incrementa intentos fallidos y bloquea 15 minutos al llegar a 5 intentos.
  - Envía correo de bloqueo si SMTP está activo.

- `Upscale-web/Views/Account/Bloqueada.cshtml`
  - Renderiza mensaje de bloqueo y contador regresivo en cliente.

- `Upscale-web/Views/Shared/_Layout.cshtml`
  - Barra superior y modal de advertencia de expiración de sesión.
  - Script para keep-alive y cierre forzado por inactividad.

## 8) Flujo funcional

### 8.1 Flujo de autenticación

1. Usuario entra a `/Account/Login`.
2. En POST `/Account/Login`, se busca por número de documento (`DNI` o `NumeroDocumento`) y tipo (`DNI/CE`).
3. Validaciones:
   - Si no existe: error de modelo.
   - Si `EstadoActivo == false`: acceso denegado.
   - Si está bloqueado (`BloqueadoHasta > now`): redirige a `/Account/Bloqueada`.
4. Si contraseña coincide:
   - Reinicia intentos y bloqueo.
   - Guarda sesión (`UserDNI`, `UserName`).
   - Redirige a `/Account/Perfil`.
5. Si contraseña no coincide:
   - Incrementa `IntentosFallidos`.
   - En el intento 5: setea `BloqueadoHasta = UtcNow + 15 min`, guarda y redirige a bloqueo.

### 8.2 Flujo de sesión

- Timeout de sesión en servidor: 20 minutos de inactividad.
- En el layout:
  - Muestra modal a los 19 minutos.
  - Cuenta regresiva de 60 segundos.
  - Opción “Extender sesión” llama a `/Account/KeepAlive`.
  - Si no hay respuesta o se agota tiempo, redirige a `/Account/Logout?expired=true`.

### 8.3 Flujo de bloqueo de cuenta

- Se envía `desbloqueoUtc` como ISO-8601 desde el controlador.
- `Bloqueada.cshtml` parsea ese valor en JavaScript y calcula tiempo restante.
- Al llegar a 0, habilita botón y redirige a login automáticamente.

## 9) Rutas y endpoints principales

### HomeController

- `GET /` o `GET /Home/Index` -> bienvenida.
- `GET /Home/Privacy` -> privacidad.

### AccountController

- `GET /Account/Login` -> formulario de login.
- `POST /Account/Login` -> autentica y gestiona bloqueos.
- `GET /Account/Perfil` -> perfil (requiere sesión).
- `GET /Account/Bloqueada?desbloqueoUtc=...` -> vista de bloqueo.
- `GET /Account/KeepAlive` -> mantiene viva la sesión.
- `GET /Account/Logout?expired=true|false` -> cierra sesión.

## 10) Modelo de datos: `Usuario`

Campos relevantes de seguridad/identidad:

- `Id`
- `DNI`, `TipoDocumento`, `NumeroDocumento`
- `Contrasena` (actualmente texto plano)
- `EstadoActivo`
- `IntentosFallidos`
- `BloqueadoHasta`
- `Email`

Campos de perfil:

- `Nombres`, `PrimerApellido`, `SegundoApellido`, `Apellidos`
- `FechaNacimiento`, `Nacionalidad`, `Sexo`
- `Telefono`, `TelefonoSecundario`
- `TipoContrato`, `Cargo`, `LugarTrabajo`, `FechaContratacion`

