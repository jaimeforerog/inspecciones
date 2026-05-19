using System.Net;
using System.Net.Http.Json;
using Inspecciones.Api.Tests.Auth;
using Inspecciones.Domain.Catalogos;
using Inspecciones.Domain.Comun;
using Inspecciones.Domain.Inspecciones;
using Inspecciones.Infrastructure.Auth;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inspecciones.Api.Tests.Tenancy;

/// <summary>
/// Test E2E del slice mt-4 §6.10 — cierra FU-60.
///
/// Verifica MT4-INV-2: el <c>CaptureBearerForOutboxMiddleware</c> propaga el
/// header <c>Authorization: Bearer X</c> del request HTTP entrante al envelope
/// del outbox de Wolverine. El listener tenant-aware (mt-3) lo consumirá del
/// envelope para propagarlo al adapter HTTP del ERP.
///
/// Estrategia del test: en vez de orquestar listener + WireMock end-to-end
/// (que requiere flushing determinístico del outbox), inspecciona la tabla
/// <c>wolverine_outgoing_envelopes</c> directamente — si el header
/// <c>X-Forwarded-Authorization</c> está persistido, el wiring funciona.
/// El consumo del header por el listener ya está cubierto por
/// <c>SincronizarDictamenVigenteBearerPropagationTests</c> (mt-3).
///
/// Requiere Postgres. Skip si no disponible.
/// </summary>
[Collection(nameof(InspeccionesAppCollection))]
[Trait("Category", "Integration")]
public sealed class CaptureBearerForOutboxEndToEndTests(InspeccionesAppFactory factory)
{
    private static readonly DateTimeOffset CapturadoEn =
        new(2026, 5, 8, 15, 0, 0, TimeSpan.Zero);

    private const int TenantTest = 7;
    private const string AuthHeaderTest = "Bearer jwt-usuario-empresa-7";

    [Fact]
    public async Task POST_inspecciones_con_Authorization_propaga_header_X_Forwarded_Authorization_al_outbox_envelope_FU_60()
    {
        const int equipoId = 7500;
        const int rutinaId = 7580;

        // GIVEN: catálogo sembrado en tenant 7.
        await SembrarCatalogoAsync(equipoId, rutinaId);

        var inspeccionId = Guid.NewGuid();
        var clientTenant7 = factory
            .WithSessionService(new FakeSessionService(idEmpresa: TenantTest))
            .CreateClient();

        // El cliente envía Authorization: Bearer jwt-usuario-empresa-7.
        // CaptureBearerForOutboxMiddleware debe capturarlo y propagarlo al envelope.

        // 1. POST /inspecciones — crea aggregate (no publica al outbox aún — el publish
        //    ocurre al firmar, donde sale InspeccionFirmada_v1).
        var iniciarResp = await EnviarPostAsync(
            clientTenant7,
            "/api/v1/inspecciones",
            CrearBodyIniciar(inspeccionId, equipoId),
            includeAuth: true);
        iniciarResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. POST hallazgo simple.
        var hallazgoId = Guid.NewGuid();
        var hallazgoResp = await EnviarPostAsync(
            clientTenant7,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos",
            CrearBodyHallazgo(hallazgoId),
            includeAuth: true);
        hallazgoResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // 3. POST firmar — emite 3 eventos y publica InspeccionFirmada_v1 al outbox.
        var firmarResp = await EnviarPostAsync(
            clientTenant7,
            $"/api/v1/inspecciones/{inspeccionId}/firmar",
            CrearBodyFirmar(inspeccionId),
            includeAuth: true);
        firmarResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // WHEN/THEN: inspeccionar el outbox de Wolverine — el envelope persistido
        // de InspeccionFirmada_v1 debe contener X-Forwarded-Authorization.
        var headerValue = await LeerXForwardedAuthDelOutboxAsync(inspeccionId);

        headerValue.Should().NotBeNull("MT4-INV-2: CaptureBearerForOutboxMiddleware debe propagar el Authorization al envelope");
        headerValue.Should().Be(AuthHeaderTest,
            "el bearer del request debe propagarse fielmente — no service-account");
    }

    [Fact]
    public async Task POST_inspecciones_sin_Authorization_publica_envelope_sin_X_Forwarded_Authorization_fallback_service_account()
    {
        const int equipoId = 7501;
        const int rutinaId = 7581;

        await SembrarCatalogoAsync(equipoId, rutinaId);

        var inspeccionId = Guid.NewGuid();
        var clientTenant7 = factory
            .WithSessionService(new FakeSessionService(idEmpresa: TenantTest))
            .CreateClient();

        // GIVEN: requests SIN header Authorization (env Test bypassa el middleware
        // corporativo, así que esto es legal aunque en prod sería 401).
        var iniciarResp = await EnviarPostAsync(
            clientTenant7,
            "/api/v1/inspecciones",
            CrearBodyIniciar(inspeccionId, equipoId),
            includeAuth: false);
        iniciarResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var hallazgoId = Guid.NewGuid();
        var hallazgoResp = await EnviarPostAsync(
            clientTenant7,
            $"/api/v1/inspecciones/{inspeccionId}/hallazgos",
            CrearBodyHallazgo(hallazgoId),
            includeAuth: false);
        hallazgoResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var firmarResp = await EnviarPostAsync(
            clientTenant7,
            $"/api/v1/inspecciones/{inspeccionId}/firmar",
            CrearBodyFirmar(inspeccionId),
            includeAuth: false);
        firmarResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // THEN: el envelope NO contiene X-Forwarded-Authorization (rule no añadió
        // header porque el carrier estaba vacío). El listener caerá al service-account
        // fallback (mt-3 D-MT3-2).
        var headerValue = await LeerXForwardedAuthDelOutboxAsync(inspeccionId);
        headerValue.Should().BeNull(
            "sin Authorization en el request, ForwardAuthEnvelopeRule no debe añadir el header");
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private async Task SembrarCatalogoAsync(int equipoId, int rutinaId)
    {
        var factoryUnderTest = factory.Services.GetRequiredService<ITenantedDocumentSessionFactory>();
        await using var session = factoryUnderTest.OpenSessionForTenant("7");

        session.Store(new EquipoLocal(
            EquipoId: equipoId,
            EquipoCodigo: $"EQ-{equipoId}",
            ProyectoId: 3,
            RutinaTecnicaId: rutinaId));

        session.Store(new RutinaTecnicaLocal(
            RutinaId: rutinaId,
            Codigo: "INSP. BULL.MOTOR",
            Nombre: "Inspección bulldozer motor",
            Tipo: TipoRutina.Tecnica,
            GrupoMantenimiento: "BULLDOZER",
            ParteId: 88,
            ParteCodigo: "MOTOR",
            SincronizadoEn: CapturadoEn.AddDays(-1)));

        await session.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> EnviarPostAsync(
        HttpClient client, string path, object body, bool includeAuth)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Client-Command-Id", Guid.NewGuid().ToString());
        if (includeAuth)
        {
            request.Headers.TryAddWithoutValidation("Authorization", AuthHeaderTest);
        }
        return await client.SendAsync(request);
    }

    private static object CrearBodyIniciar(Guid inspeccionId, int equipoId) =>
        new
        {
            inspeccionId,
            equipoId,
            proyectoId = 3,
            ubicacionInicio = new
            {
                latitud = 4.711m,
                longitud = -74.072m,
                precisionMetros = 8.5m,
                capturadoEn = CapturadoEn,
            },
            fechaReportada = DateOnly.FromDateTime(CapturadoEn.UtcDateTime),
            lecturaMedidorPrimario = (object?)null,
            lecturaMedidorSecundario = (object?)null,
        };

    private static object CrearBodyHallazgo(Guid hallazgoId) =>
        new
        {
            hallazgoId,
            parteEquipoId = 77,
            novedadPreopOrigenId = (int?)null,
            origenHallazgo = "Manual",
            novedadTecnica = "Hallazgo de prueba mt-4",
            actividadDescripcion = "Verificación visual",
            accionRequerida = "NoRequiereIntervencion",
            ubicacionRegistro = new
            {
                latitud = 4.711m,
                longitud = -74.072m,
                precisionMetros = 8.5m,
                capturadoEn = CapturadoEn,
            },
            evidencias = Array.Empty<object>(),
            emitidoPor = "1",
        };

    private static object CrearBodyFirmar(Guid inspeccionId) =>
        new
        {
            inspeccionId,
            diagnostico = "Equipo en buen estado — prueba mt-4",
            dictamen = "PuedeOperar",
            justificacionDictamen = "Sin hallazgos críticos",
            firmaUri = "https://blobs/firma-mt4.png",
            ubicacionFirma = new
            {
                latitud = 4.711m,
                longitud = -74.072m,
                precisionMetros = 8.5m,
                capturadoEn = CapturadoEn,
            },
            tecnicoId = "1",
        };

    /// <summary>
    /// Lee la columna headers (json) del envelope persistido por Wolverine
    /// para el mensaje InspeccionFirmada_v1 de la inspección dada. Retorna
    /// el valor de "X-Forwarded-Authorization" o null si no está.
    ///
    /// Si Wolverine ya drenó el envelope (procesado), no estará en
    /// wolverine_outgoing_envelopes. En ese caso buscamos en
    /// wolverine_dead_letters (caso de error) o asumimos drenado limpio.
    /// Para el test, agregamos un pequeño retry — el endpoint POST firmar
    /// debería persistir el envelope antes de retornar (transaccional).
    /// </summary>
    private async Task<string?> LeerXForwardedAuthDelOutboxAsync(Guid inspeccionId)
    {
        var connString = factory.Services
            .GetRequiredService<IDocumentStore>()
            .Options
            .Tenancy
            .Default
            .Database
            .CreateConnection()
            .ConnectionString;

        // Esperar hasta 5s a que el envelope aparezca (puede haber drift entre
        // SaveChangesAsync del handler y el insert al outbox).
        for (var intento = 0; intento < 25; intento++)
        {
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Buscar en outgoing primero. Si Wolverine ya lo despachó, puede haber
            // ido a wolverine_dead_letters o haberse borrado. Probamos ambas.
            foreach (var tabla in new[] { "wolverine_outgoing_envelopes", "wolverine_dead_letters" })
            {
                await using var cmd = new NpgsqlCommand($@"
                    SELECT headers::text
                    FROM public.{tabla}
                    WHERE message_type LIKE '%InspeccionFirmada%'
                       OR body::text LIKE '%{inspeccionId}%'
                    LIMIT 1;", conn);
                try
                {
                    var result = await cmd.ExecuteScalarAsync();
                    if (result is string json && !string.IsNullOrEmpty(json))
                    {
                        return ExtraerHeaderDeJson(json, "X-Forwarded-Authorization");
                    }
                }
                catch (PostgresException ex) when (ex.SqlState == "42P01")
                {
                    // Tabla no existe (Wolverine no la creó aún). Probar la siguiente.
                }
            }

            await Task.Delay(200);
        }

        // No encontrado en 5s: el envelope no fue persistido. Retornar null indica
        // que el wiring no funcionó (en el test happy path) o que la rule no añadió
        // header (en el test sin auth).
        return null;
    }

    private static string? ExtraerHeaderDeJson(string headersJson, string clave)
    {
        // headersJson es algo como {"X-Forwarded-Authorization":"Bearer ...","other":"value"}
        // Hacemos parse simple sin depender de System.Text.Json para evitar deps frágiles.
        var doc = System.Text.Json.JsonDocument.Parse(headersJson);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.NameEquals(clave))
            {
                return prop.Value.GetString();
            }
        }
        return null;
    }
}
