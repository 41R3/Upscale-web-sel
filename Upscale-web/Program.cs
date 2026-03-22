using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using System.Net.Sockets;
using Upscale_web.Data;

var builder = WebApplication.CreateBuilder(args);

var sessionIdleTimeoutMinutes = builder.Configuration.GetValue<int?>("Session:IdleTimeoutMinutes") ?? 20;
if (sessionIdleTimeoutMinutes <= 0)
{
    sessionIdleTimeoutMinutes = 20;
}

// 1. Configurar la conexión a SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Configurar Sesiones (Para el timeout de 20 minutos y login)
builder.Services.AddSession(options => {
   options.IdleTimeout = TimeSpan.FromMinutes(sessionIdleTimeoutMinutes);
 // options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllersWithViews();

var app = builder.Build();
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    startupLogger.LogError("No se encontró ConnectionStrings:DefaultConnection.");
}
else
{
    try
    {
        var csb = new SqlConnectionStringBuilder(connectionString);
        var dataSource = csb.DataSource ?? string.Empty;
        var host = dataSource;
        var port = 1433;

        if (dataSource.Contains(','))
        {
            var serverParts = dataSource.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            host = serverParts[0];
            if (serverParts.Length > 1 && int.TryParse(serverParts[1], out var parsedPort))
            {
                port = parsedPort;
            }
        }

        using var tcp = new TcpClient();
        var connectTask = tcp.ConnectAsync(host, port);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3));
        var completedTask = await Task.WhenAny(connectTask, timeoutTask);

        if (completedTask == connectTask && tcp.Connected)
        {
            startupLogger.LogInformation("Conectividad TCP OK con SQL Server {Host}:{Port}", host, port);
        }
        else
        {
            startupLogger.LogWarning("No se pudo abrir conexión TCP a SQL Server {Host}:{Port}. Verifica red/firewall/puerto.", host, port);
        }

        startupLogger.LogInformation("Sesión configurada con IdleTimeout={IdleTimeoutMinutes} minutos", sessionIdleTimeoutMinutes);
    }
    catch (Exception ex)
    {
        startupLogger.LogWarning(ex, "No se pudo analizar ConnectionStrings:DefaultConnection.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); //  para cargar Bootstrap/CSS

app.UseRouting();

// 3. Habilitar el uso de sesiones ANTES de la autorización
app.UseSession(); 

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
        {
            startupLogger.LogError("No se pudo conectar a SQL Server con la cadena DefaultConnection. Revisa host/puerto/credenciales.");
        }

        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('dbo.Usuarios', 'Apellidos') IS NULL ALTER TABLE dbo.Usuarios ADD Apellidos NVARCHAR(200) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'PrimerApellido') IS NULL ALTER TABLE dbo.Usuarios ADD PrimerApellido NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'SegundoApellido') IS NULL ALTER TABLE dbo.Usuarios ADD SegundoApellido NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'TipoDocumento') IS NULL ALTER TABLE dbo.Usuarios ADD TipoDocumento NVARCHAR(20) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'NumeroDocumento') IS NULL ALTER TABLE dbo.Usuarios ADD NumeroDocumento NVARCHAR(50) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'FechaNacimiento') IS NULL ALTER TABLE dbo.Usuarios ADD FechaNacimiento DATETIME2 NULL;
            IF COL_LENGTH('dbo.Usuarios', 'Nacionalidad') IS NULL ALTER TABLE dbo.Usuarios ADD Nacionalidad NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'Sexo') IS NULL ALTER TABLE dbo.Usuarios ADD Sexo NVARCHAR(20) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'Telefono') IS NULL ALTER TABLE dbo.Usuarios ADD Telefono NVARCHAR(30) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'TelefonoSecundario') IS NULL ALTER TABLE dbo.Usuarios ADD TelefonoSecundario NVARCHAR(30) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'TipoContrato') IS NULL ALTER TABLE dbo.Usuarios ADD TipoContrato NVARCHAR(50) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'Cargo') IS NULL ALTER TABLE dbo.Usuarios ADD Cargo NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'LugarTrabajo') IS NULL ALTER TABLE dbo.Usuarios ADD LugarTrabajo NVARCHAR(150) NULL;
            IF COL_LENGTH('dbo.Usuarios', 'EstadoActivo') IS NULL ALTER TABLE dbo.Usuarios ADD EstadoActivo BIT NOT NULL CONSTRAINT DF_Usuarios_EstadoActivo DEFAULT(1);
            IF COL_LENGTH('dbo.Usuarios', 'FechaContratacion') IS NULL ALTER TABLE dbo.Usuarios ADD FechaContratacion DATETIME2 NULL;
        """);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Aviso: no se pudo sincronizar el esquema de la tabla Usuarios. {ex.Message}");
    }
}

app.Run();