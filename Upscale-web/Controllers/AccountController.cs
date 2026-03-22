using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Upscale_web.Data;
using Upscale_web.Models;

namespace Upscale_web.Controllers;

public class AccountController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AccountController> _logger;

    public AccountController(ApplicationDbContext context, IConfiguration configuration, ILogger<AccountController> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    // GET: /Account/Login
    [HttpGet]
    public IActionResult Login() => View();

    // POST: /Account/Login
    [HttpPost]
    public async Task<IActionResult> Login(string dni, string password, string documentType)
    {
        var tipoDocumento = string.IsNullOrWhiteSpace(documentType)
            ? "DNI"
            : documentType.Trim().ToUpperInvariant();

        var documento = dni?.Trim() ?? string.Empty;

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u =>
            (u.DNI == documento || u.NumeroDocumento == documento) &&
            (string.IsNullOrWhiteSpace(u.TipoDocumento) || u.TipoDocumento.Trim().ToUpper() == tipoDocumento));

        if (usuario == null)
        {
            ModelState.AddModelError("", "El usuario no existe.");
            return View();
        }

        if (!usuario.EstadoActivo)
        {
            ModelState.AddModelError("", "Su cuenta aún no está activa. Contacte al soporte.");
            return View(usuario);
        }

        var now = DateTime.UtcNow;

        if (usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta <= now)
        {
            usuario.BloqueadoHasta = null;
            usuario.IntentosFallidos = 0;
            await _context.SaveChangesAsync();
        }

        // Lógica de Bloqueo (Flujo del Figma)
        if (usuario.BloqueadoHasta.HasValue && usuario.BloqueadoHasta > now)
        {
            return RedirectToAction(nameof(Bloqueada), new
            {
                desbloqueoUtc = FormatUnlockAtUtc(usuario.BloqueadoHasta.Value)
            });
        }

        if (usuario.Contrasena == password) 
        {
            // Reset de intentos fallidos
            usuario.IntentosFallidos = 0;
            usuario.BloqueadoHasta = null;
            await _context.SaveChangesAsync();

            // Guardar en Sesión
            HttpContext.Session.SetString("UserDNI", usuario.DNI ?? usuario.NumeroDocumento ?? string.Empty);
            HttpContext.Session.SetString("UserName", usuario.NombreCompleto);

            return RedirectToAction(nameof(Perfil));
        }
        else
        {
            usuario.IntentosFallidos++;
            if (usuario.IntentosFallidos >= 5)
            {
                usuario.IntentosFallidos = 5;
                usuario.BloqueadoHasta = DateTime.UtcNow.AddMinutes(15);
                await _context.SaveChangesAsync();
                await EnviarCorreoBloqueoAsync(usuario);
                return RedirectToAction(nameof(Bloqueada), new
                {
                    desbloqueoUtc = FormatUnlockAtUtc(usuario.BloqueadoHasta.Value)
                });
            }
            else
            {
                await _context.SaveChangesAsync();
                ModelState.AddModelError("", $"Contraseña incorrecta. Intento {usuario.IntentosFallidos} de 5.");
            }

            return View(usuario);
        }
    }

    // GET: /Account/Perfil
    public async Task<IActionResult> Perfil()
    {
        var dni = HttpContext.Session.GetString("UserDNI");
        if (string.IsNullOrWhiteSpace(dni)) return RedirectToAction(nameof(Login));

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.DNI == dni || u.NumeroDocumento == dni);
        if (usuario == null)
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login), new { expired = true });
        }

        return View(usuario);
    }

    [HttpGet]
    public IActionResult Bloqueada(string? desbloqueoUtc = null)
    {
        ViewData["ServerNowUtc"] = DateTime.UtcNow.ToString("O");
        return View("Bloqueada", model: desbloqueoUtc);
    }

    [HttpGet]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult KeepAlive()
    {
        var dni = HttpContext.Session.GetString("UserDNI");
        var name = HttpContext.Session.GetString("UserName");

        if (!string.IsNullOrWhiteSpace(dni))
        {
            HttpContext.Session.SetString("UserDNI", dni);

            if (!string.IsNullOrWhiteSpace(name))
            {
                HttpContext.Session.SetString("UserName", name);
            }
        }

        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers["X-Server-Utc"] = DateTime.UtcNow.ToString("O");

        return NoContent();
    }

    [HttpGet]
    public IActionResult Logout(bool expired = false)
    {
        HttpContext.Session.Clear();
        return RedirectToAction(nameof(Login), new { expired });
    }

    private async Task EnviarCorreoBloqueoAsync(Usuario usuario)
    {
        var smtpSection = _configuration.GetSection("Smtp");
        var host = smtpSection["Host"];
        var from = smtpSection["From"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(usuario.Email))
        {
            _logger.LogWarning("No se envió el correo de bloqueo porque falta configuración SMTP o el correo del usuario.");
            return;
        }

        if (!int.TryParse(smtpSection["Port"], out var port))
        {
            port = 587;
        }

        var enableSsl = bool.TryParse(smtpSection["EnableSsl"], out var ssl) && ssl;
        var userName = smtpSection["UserName"];
        var password = smtpSection["Password"];
        var fromName = smtpSection["FromName"] ?? "Sistema de Gestión";
        var loginUrl = $"{Request.Scheme}://{Request.Host}{Url.Action(nameof(Login), "Account")}";

        var body = $@"
            <h2>Su cuenta fue bloqueada temporalmente</h2>
            <p>Hola {WebUtility.HtmlEncode(usuario.NombreCompleto)},</p>
            <p>Detectamos demasiados intentos fallidos de inicio de sesión. Por seguridad, su cuenta quedó bloqueada durante 15 minutos.</p>
            <p>Podrá volver a ingresar una vez que el bloqueo expire.</p>
            <p><a href='{loginUrl}' style='display:inline-block;padding:12px 18px;background:#0056b3;color:#fff;text-decoration:none;border-radius:6px;'>Volver al inicio de sesión</a></p>
        ";

        using var message = new MailMessage
        {
            From = new MailAddress(from, fromName),
            Subject = "Bloqueo temporal de cuenta",
            Body = body,
            IsBodyHtml = true
        };
        message.To.Add(usuario.Email);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            Credentials = string.IsNullOrWhiteSpace(userName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(userName, password)
        };

        try
        {
            await client.SendMailAsync(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo enviar el correo de bloqueo para {Email}", usuario.Email);
        }
    }

    private static string FormatUnlockAtUtc(DateTime unlockAt)
    {
        // SQL Server devuelve DateTime con Kind=Unspecified; lo tratamos como UTC para no sumar el offset local.
        var utcUnlockAt = unlockAt.Kind switch
        {
            DateTimeKind.Utc => unlockAt,
            DateTimeKind.Local => unlockAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(unlockAt, DateTimeKind.Utc)
        };

        return utcUnlockAt.ToString("O");
    }
}