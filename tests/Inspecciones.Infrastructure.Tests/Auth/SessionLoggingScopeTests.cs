using FluentAssertions;
using Inspecciones.Infrastructure.Auth;
using Microsoft.Extensions.Logging;

namespace Inspecciones.Infrastructure.Tests.Auth;

/// <summary>
/// Tests rojos del slice mt-4 §6.11 — observabilidad por IdEmpresa (MT4-INV-3).
///
/// Verifica que <c>logger.BeginEmpresaScope(session)</c> abre un scope con
/// <c>IdEmpresa</c> e <c>IdUsuario</c> como propiedades estructuradas. El
/// scope debe restaurarse al dispose. Si el session service lanza
/// <c>ClaimRequeridaException</c> al acceder a IdEmpresa, el helper devuelve
/// null (no enriquece el scope — pre-auth no debe enriquecerse).
/// </summary>
public sealed class SessionLoggingScopeTests
{
    [Fact]
    public void BeginEmpresaScope_con_session_valida_setea_propiedades_IdEmpresa_e_IdUsuario()
    {
        // GIVEN
        var session = new FakeSession(idEmpresa: 7, idUsuario: 42);
        var captor = new ScopeCaptor();
        var logger = new TestLogger(captor);

        // WHEN
        using (logger.BeginEmpresaScope(session))
        {
            // Forzar emisión de una entrada para que el captor registre el scope.
#pragma warning disable CA1848 // test scaffolding
            logger.LogInformation("evento de prueba dentro del scope");
#pragma warning restore CA1848
        }

        // THEN
        captor.LastEntryScopes.Should().NotBeNull();
        var scope = captor.LastEntryScopes!;
        scope.Should().ContainKey("IdEmpresa");
        scope["IdEmpresa"].Should().Be(7);
        scope.Should().ContainKey("IdUsuario");
        scope["IdUsuario"].Should().Be(42);
    }

    [Fact]
    public void BeginEmpresaScope_con_session_que_lanza_ClaimRequerida_retorna_null_y_no_propaga()
    {
        // GIVEN: session que falla al leer IdEmpresa (caso pre-auth).
        var session = new FakeSession(idEmpresa: 0, idUsuario: 0, lanzarAlLeerIdEmpresa: true);
        var captor = new ScopeCaptor();
        var logger = new TestLogger(captor);

        // WHEN/THEN: no lanza, retorna null.
        IDisposable? scope = null;
        var act = () => scope = logger.BeginEmpresaScope(session);
        act.Should().NotThrow("pre-auth no debe romper el endpoint — el 401 lo da el middleware");
        scope.Should().BeNull();
    }

    // ─── Test doubles ──────────────────────────────────────────────────────

    private sealed class FakeSession : ISessionService
    {
        private readonly int _idEmpresa;
        private readonly int _idUsuario;
        private readonly bool _lanzar;

        public FakeSession(int idEmpresa, int idUsuario, bool lanzarAlLeerIdEmpresa = false)
        {
            _idEmpresa = idEmpresa;
            _idUsuario = idUsuario;
            _lanzar = lanzarAlLeerIdEmpresa;
        }

        public int IdEmpresa => _lanzar
            ? throw new ClaimRequeridaException("IdEmpresa")
            : _idEmpresa;
        public int IdUsuario => _idUsuario;
        public string NomUsuario => "test";
        public int IdSucursal => 0;
        public int IdProyecto => 0;
        public IReadOnlyCollection<string> Capabilities => Array.Empty<string>();
    }

    private sealed class ScopeCaptor
    {
        public IReadOnlyDictionary<string, object?>? LastEntryScopes { get; set; }
    }

    private sealed class TestLogger : ILogger
    {
        private readonly ScopeCaptor _captor;
        private readonly AsyncLocal<Dictionary<string, object?>> _currentScope = new();

        public TestLogger(ScopeCaptor captor) { _captor = captor; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                _currentScope.Value = kvps.ToDictionary(k => k.Key, v => v.Value);
            }
            return new Restorer(this);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _captor.LastEntryScopes = _currentScope.Value;
        }

        private sealed class Restorer : IDisposable
        {
            private readonly TestLogger _logger;
            public Restorer(TestLogger logger) { _logger = logger; }
            public void Dispose() { _logger._currentScope.Value = null!; }
        }
    }
}
