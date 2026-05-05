namespace Inspecciones.Application;

/// <summary>
/// Marker público para que Wolverine pueda hacer assembly scanning de
/// handlers, sagas y mensajes definidos en este proyecto desde el host
/// (<c>opts.Discovery.IncludeAssembly(typeof(AssemblyMarker).Assembly)</c>).
/// </summary>
public static class AssemblyMarker;
