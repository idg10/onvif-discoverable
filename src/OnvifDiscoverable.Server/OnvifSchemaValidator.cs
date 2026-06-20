using System.Reflection;
using System.Xml;
using System.Xml.Schema;

namespace OnvifDiscoverable.Server;

/// <summary>
/// Validates outgoing ONVIF SOAP responses against the official ONVIF/WS-* XML schemas,
/// which are bundled as embedded resources so no network access is required at runtime.
/// Intended as a development aid: it surfaces schema mistakes (wrong element order, missing
/// required elements, attributes-vs-elements confusion, etc.) at the moment a response is
/// produced, rather than leaving them to be rejected silently by a strict client such as the
/// Windows WS-Management stack (which reports them only as <c>WS_E_INVALID_FORMAT</c>).
/// </summary>
internal sealed class OnvifSchemaValidator
{
    // Maps the final path segment of a schemaLocation URI to the bundled schema file name.
    // Lets us resolve every import/include — local or external — to an embedded resource by
    // file name alone, regardless of the (synthetic or real) base URI it was requested under.
    private static readonly Dictionary<string, string> SchemaFileByToken = new(StringComparer.OrdinalIgnoreCase)
    {
        ["onvif.xsd"] = "onvif.xsd",
        ["common.xsd"] = "common.xsd",
        ["tds.xsd"] = "tds.xsd",
        ["trt.xsd"] = "trt.xsd",
        ["xml.xsd"] = "xml.xsd",
        ["b-2.xsd"] = "b-2.xsd",
        ["bf-2.xsd"] = "bf-2.xsd",
        ["t-1.xsd"] = "t-1.xsd",
        ["ws-addr.xsd"] = "ws-addr.xsd",
        ["xmlmime"] = "xmlmime.xsd",
        ["soap-envelope"] = "soap-envelope.xsd",
        ["include"] = "xop-include.xsd",
    };

    // The three schema roots to load explicitly. All share one synthetic base directory so
    // that the relative "onvif.xsd" import in tds.xsd/trt.xsd resolves to the *same* absolute
    // URI as the onvif.xsd root — otherwise the schema set would load onvif.xsd more than once
    // and report every global element as a duplicate declaration.
    private const string SchemaBaseDir = "http://schemas.local/onvif/";
    private static readonly (string Namespace, string File)[] Roots =
    [
        ("http://www.onvif.org/ver10/schema", "onvif.xsd"),
        ("http://www.onvif.org/ver10/device/wsdl", "tds.xsd"),
        ("http://www.onvif.org/ver10/media/wsdl", "trt.xsd"),
    ];

    private readonly XmlSchemaSet _schemas;

    private OnvifSchemaValidator(XmlSchemaSet schemas) => _schemas = schemas;

    /// <summary>
    /// Compiles the bundled schema set. Returns <c>null</c> (with a logged warning) if the
    /// schemas cannot be loaded, so that a validation-setup problem never stops the server.
    /// </summary>
    public static OnvifSchemaValidator? TryCreate()
    {
        try
        {
            var resolver = new EmbeddedSchemaResolver();
            var schemas = new XmlSchemaSet { XmlResolver = resolver };
            // The ONVIF schemas use wildcard extension points that trip .NET's Unique Particle
            // Attribution check; it is not relevant to validating our (simple) responses.
            schemas.CompilationSettings.EnableUpaCheck = false;

            var errors = new List<string>();
            schemas.ValidationEventHandler += (_, e) =>
            {
                if (e.Severity == XmlSeverityType.Error)
                {
                    errors.Add(e.Message);
                }
            };

            foreach (var (ns, file) in Roots)
            {
                using var stream = OpenSchema(file);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { XmlResolver = resolver }, SchemaBaseDir + file);
                schemas.Add(ns, reader);
            }

            schemas.Compile();

            if (errors.Count > 0)
            {
                Console.Error.WriteLine($"Schema validation disabled: schema set failed to compile ({errors.Count} errors). First: {errors[0]}");
                return null;
            }

            return new OnvifSchemaValidator(schemas);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Schema validation disabled: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validates a SOAP response document against the schema set. Returns the list of schema
    /// violations; an empty list means the response is schema-valid.
    /// </summary>
    public IReadOnlyList<string> Validate(string responseXml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas,
            XmlResolver = null,
        };
        settings.ValidationEventHandler += (_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error)
            {
                errors.Add(e.Message);
            }
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(responseXml), settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException ex)
        {
            errors.Add($"Malformed XML: {ex.Message}");
        }

        return errors;
    }

    private static Stream OpenSchema(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        // Embedded resource names are "<RootNamespace>.Schemas.<fileName>"; match by suffix
        // so we don't depend on the exact root-namespace prefix.
        string suffix = ".Schemas." + fileName;
        string? resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            n => n.EndsWith(suffix, StringComparison.Ordinal));

        if (resourceName is null)
        {
            throw new FileNotFoundException($"Embedded schema resource not found for '{fileName}'.");
        }

        return assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded schema resource stream null for '{fileName}'.");
    }

    /// <summary>
    /// Resolves schema imports/includes to bundled embedded resources by file-name token,
    /// so the schema set compiles with no network access.
    /// </summary>
    private sealed class EmbeddedSchemaResolver : XmlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            string token = absoluteUri.Segments[^1].TrimEnd('/');
            if (!SchemaFileByToken.TryGetValue(token, out string? file))
            {
                throw new XmlException($"Unexpected schema reference '{absoluteUri}' (token '{token}') — not a bundled schema.");
            }

            return OpenSchema(file);
        }
    }
}
