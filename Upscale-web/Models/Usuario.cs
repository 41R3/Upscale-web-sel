using System.ComponentModel.DataAnnotations.Schema;

namespace Upscale_web.Models;

public class Usuario
{
    public int Id { get; set; }
    public string? DNI { get; set; }
    public string? Contrasena { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? PrimerApellido { get; set; }
    public string? SegundoApellido { get; set; }
    public string? TipoDocumento { get; set; }
    public string? NumeroDocumento { get; set; }
    public DateTime? FechaNacimiento { get; set; }
    public string? Nacionalidad { get; set; }
    public string? Sexo { get; set; }
    public string? Email { get; set; }
    public string? Telefono { get; set; }
    public string? TelefonoSecundario { get; set; }
    public string? TipoContrato { get; set; }
    public string? Cargo { get; set; }
    public string? LugarTrabajo { get; set; }
    public bool EstadoActivo { get; set; } = true;
    public int IntentosFallidos { get; set; }
    public DateTime? BloqueadoHasta { get; set; }
    public DateTime? FechaContratacion { get; set; }

    [NotMapped]
    public string NombreCompleto => string.Join(" ", new[] { Nombres, PrimerApellido, SegundoApellido }
        .Where(part => !string.IsNullOrWhiteSpace(part)));

    [NotMapped]
    public string ApellidosCompletos
    {
        get
        {
            var apellidos = string.Join(" ", new[] { PrimerApellido, SegundoApellido }
                .Where(part => !string.IsNullOrWhiteSpace(part)));

            return string.IsNullOrWhiteSpace(apellidos) ? (Apellidos ?? string.Empty) : apellidos;
        }
    }

    [NotMapped]
    public string DocumentoCompleto => $"{(string.IsNullOrWhiteSpace(TipoDocumento) ? "DNI" : TipoDocumento)} {(NumeroDocumento ?? DNI ?? string.Empty)}".Trim();
}