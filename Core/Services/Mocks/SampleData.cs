using System;
using System.Collections.Generic;
using Iris.Core.Models;

namespace Iris.Core.Services.Mocks;

/// <summary>
/// Demo data (IRIS_SPEC §11). Hymn and Bible texts are public domain (RVR1909 and 19th-century translations).
/// </summary>
public static class SampleData
{
    public static readonly UserSession Session = new(Guid.Parse("6f1c2b8e-0d55-4a5e-9a6b-0c7f3f1d9a11"), "Iglesia Vida Nueva", "Daniel Ruiz", "pastor@vidanueva.org");

    public static IReadOnlyList<ShowcaseItem> Showcase { get; } =
    [
        new("Sublime gracia del Señor\nque a un pecador salvó;\nfui ciego mas hoy veo yo,\nperdido y Él me halló.", "Sublime gracia · Estrofa 1"),
        new("Lámpara es a mis pies tu palabra,\ny lumbrera a mi camino.", "Salmos 119:105"),
        new("¡Santo, santo, santo!\nSeñor omnipotente.", "Santo, santo, santo"),
        new("Venid a mí todos los que estáis trabajados y cargados, y yo os haré descansar.", "Mateo 11:28"),
    ];

    public static IReadOnlyList<ProjectionBackground> Backgrounds { get; } =
    [
        new("aurora", "Aurora", ["#2A1658", "#4E2A8C", "#131E5C"], true),
        new("brasa", "Brasa", ["#3A1E08", "#8C3A1E", "#3D1235"], false),
        new("oceano", "Océano", ["#06283D", "#0E5E6F", "#0A1931"], true),
        new("olivo", "Olivo", ["#0F2417", "#2F5233", "#111A12"], false),
        new("alba", "Alba", ["#5B2A3C", "#C0694E", "#2B1A3A"], false),
        new("medianoche", "Medianoche", ["#07070B", "#15151F", "#07070B"], false),
    ];

    // ----- Hymns -----

    public static IReadOnlyList<Slide> SublimeGracia() =>
    [
        Slide.Create(new TextContent("Sublime gracia del Señor\nque a un pecador salvó;\nfui ciego mas hoy veo yo,\nperdido y Él me halló."), "Estrofa 1"),
        Slide.Create(new TextContent("Su gracia me enseñó a temer,\nmis dudas ahuyentó;\n¡oh cuán precioso fue a mi ser\ncuando Él me transformó!"), "Estrofa 2"),
        Slide.Create(new TextContent("En los peligros o aflicción\nque yo he tenido aquí,\nsu gracia siempre me libró\ny me guiará feliz."), "Estrofa 3"),
        Slide.Create(new TextContent("Y cuando en Sion por siglos mil\nbrillando esté cual sol,\nyo cantaré por siempre allí\nsu amor que me salvó."), "Estrofa 4"),
    ];

    public static IReadOnlyList<Slide> SantoSantoSanto() =>
    [
        Slide.Create(new TextContent("¡Santo, santo, santo! Señor omnipotente,\nsiempre el labio mío loores te dará;\n¡santo, santo, santo! te adoro reverente,\nDios en tres personas, bendita Trinidad."), "Estrofa 1"),
        Slide.Create(new TextContent("¡Santo, santo, santo! en numeroso coro,\nsantos escogidos te adoran sin cesar,\nde alegría llenos, y sus coronas de oro\nrinden ante el trono y el cristalino mar."), "Estrofa 2"),
    ];

    public static IReadOnlyList<Slide> CastilloFuerte() =>
    [
        Slide.Create(new TextContent("Castillo fuerte es nuestro Dios,\ndefensa y buen escudo;\ncon su poder nos librará\nen este trance agudo.")),
        Slide.Create(new TextContent("Con furia y con afán\nacósanos Satán;\npor armas deja ver\nastucia y gran poder;\ncual él no hay en la tierra.")),
    ];

    private static IReadOnlyList<Slide> OhQueAmigo() =>
    [
        Slide.Create(new TextContent("¡Oh, qué amigo nos es Cristo!\nÉl llevó nuestro dolor,\ny nos manda que llevemos\ntodo a Dios en oración."), "Estrofa 1"),
    ];

    private static IReadOnlyList<Slide> RocaDeLaEternidad() =>
    [
        Slide.Create(new TextContent("Roca de la eternidad,\nfuiste abierta tú por mí;\nsé mi escondedero fiel,\nsolo encuentro paz en ti."), "Estrofa 1"),
    ];

    private static IReadOnlyList<Slide> CarinosoSalvador() =>
    [
        Slide.Create(new TextContent("Cariñoso Salvador,\nhuyo de la tempestad\na tu seno protector,\nfiándome de tu bondad."), "Estrofa 1"),
    ];

    // ----- Service -----

    public static ServicePlan SundayService()
    {
        var today = DateTimeOffset.Now.Date;
        return new ServicePlan(Guid.NewGuid(), "Servicio dominical", new DateTimeOffset(today.AddHours(10)),
        [
            new(Guid.NewGuid(), ServiceItemKind.Announcement, "Bienvenida", "Anuncios de la semana",
            [
                Slide.Create(new TextContent("Bienvenidos a casa", "Iglesia Vida Nueva")),
                Slide.Create(new TextContent("Cena congregacional\nSábado · 7:00 p. m.", "Salón principal")),
            ]),
            new(Guid.NewGuid(), ServiceItemKind.Song, "Sublime gracia", "John Newton", SublimeGracia()),
            new(Guid.NewGuid(), ServiceItemKind.Song, "Santo, santo, santo", "Reginald Heber · trad. Juan B. Cabrera", SantoSantoSanto()),
            new(Guid.NewGuid(), ServiceItemKind.Scripture, "Salmos 23:1-4", "Reina-Valera 1909",
            [
                Slide.Create(new TextContent(Psalm23[0], "Salmos 23:1"), "v. 1"),
                Slide.Create(new TextContent(Psalm23[1], "Salmos 23:2"), "v. 2"),
                Slide.Create(new TextContent(Psalm23[2], "Salmos 23:3"), "v. 3"),
                Slide.Create(new TextContent(Psalm23[3], "Salmos 23:4"), "v. 4"),
            ]),
            new(Guid.NewGuid(), ServiceItemKind.Video, "Testimonios de bautismo", "Video · 2:45",
            [
                Slide.Create(new VideoContent("Testimonios de bautismo", TimeSpan.FromSeconds(165))),
            ]),
            new(Guid.NewGuid(), ServiceItemKind.Song, "Castillo fuerte", "Martín Lutero · trad. Juan B. Cabrera", CastilloFuerte()),
        ]);
    }

    // ----- Library -----

    public static IReadOnlyList<LyricSheet> Lyrics { get; } =
    [
        new(Guid.NewGuid(), "Oh, qué amigo nos es Cristo", "Joseph M. Scriven", OhQueAmigo()),
        new(Guid.NewGuid(), "Roca de la eternidad", "Augustus M. Toplady", RocaDeLaEternidad()),
        new(Guid.NewGuid(), "Cariñoso Salvador", "Charles Wesley", CarinosoSalvador()),
        new(Guid.NewGuid(), "Sublime gracia", "John Newton", SublimeGracia()),
        new(Guid.NewGuid(), "Santo, santo, santo", "Reginald Heber", SantoSantoSanto()),
        new(Guid.NewGuid(), "Castillo fuerte", "Martín Lutero", CastilloFuerte()),
    ];

    public static IReadOnlyList<MediaAsset> Music { get; } =
    [
        new(Guid.NewGuid(), MediaKind.Music, "Piano de fondo", "Ambiente para oración", TimeSpan.FromSeconds(372), ["#0F2417", "#3DDC97"]),
        new(Guid.NewGuid(), MediaKind.Music, "Preludio en Re", "Órgano · inicio del servicio", TimeSpan.FromSeconds(220), ["#131E5C", "#3DDC97"]),
        new(Guid.NewGuid(), MediaKind.Music, "Ofrenda", "Guitarra acústica", TimeSpan.FromSeconds(245), ["#3A1E08", "#3DDC97"]),
        new(Guid.NewGuid(), MediaKind.Music, "Pads de adoración en Sol", "Ambiente continuo", TimeSpan.FromSeconds(600), ["#2A1658", "#3DDC97"]),
        new(Guid.NewGuid(), MediaKind.Music, "Sublime gracia (instrumental)", "Piano y cuerdas", TimeSpan.FromSeconds(272), ["#3D1235", "#3DDC97"]),
    ];

    private const string ImageSubtitle = "JPG · 1920 × 1080";

    public static IReadOnlyList<MediaAsset> Images { get; } =
    [
        new(Guid.NewGuid(), MediaKind.Image, "Logo de la iglesia", ImageSubtitle, null, ["#2A1658", "#9B5CFF", "#FF7A59"]),
        new(Guid.NewGuid(), MediaKind.Image, "Bienvenida", ImageSubtitle, null, ["#3A1E08", "#FFB547", "#FF7A59"]),
        new(Guid.NewGuid(), MediaKind.Image, "Santa Cena", ImageSubtitle, null, ["#3D1235", "#8C3A1E", "#07070B"]),
        new(Guid.NewGuid(), MediaKind.Image, "Bautismos", ImageSubtitle, null, ["#06283D", "#0E5E6F", "#4E5BFF"]),
        new(Guid.NewGuid(), MediaKind.Image, "Jóvenes", ImageSubtitle, null, ["#F0508C", "#9B5CFF", "#131E5C"]),
        new(Guid.NewGuid(), MediaKind.Image, "Misiones", ImageSubtitle, null, ["#0F2417", "#2F5233", "#C0694E"]),
    ];

    public static IReadOnlyList<MediaAsset> Videos { get; } =
    [
        new(Guid.NewGuid(), MediaKind.Video, "Testimonios de bautismo", "MP4 · 1080p", TimeSpan.FromSeconds(165), ["#06283D", "#0E5E6F", "#0A1931"]),
        new(Guid.NewGuid(), MediaKind.Video, "Cuenta regresiva", "MP4 · 1080p", TimeSpan.FromSeconds(300), ["#3A1E08", "#FF7A59", "#3D1235"]),
        new(Guid.NewGuid(), MediaKind.Video, "Anuncios de octubre", "MP4 · 1080p", TimeSpan.FromSeconds(90), ["#2A1658", "#4E5BFF", "#131E5C"]),
        new(Guid.NewGuid(), MediaKind.Video, "Misión en Oaxaca", "MP4 · 4K", TimeSpan.FromSeconds(252), ["#5B2A3C", "#C0694E", "#2B1A3A"]),
        new(Guid.NewGuid(), MediaKind.Video, "Video de bienvenida", "MP4 · 1080p", TimeSpan.FromSeconds(45), ["#3D1235", "#F0508C", "#FFB547"]),
    ];

    // ----- Bible (RVR1909, spelling modernized) -----

    public static readonly string[] Psalm23 =
    [
        "Jehová es mi pastor; nada me faltará.",
        "En lugares de delicados pastos me hará yacer: junto a aguas de reposo me pastoreará.",
        "Confortará mi alma; guiárame por sendas de justicia por amor de su nombre.",
        "Aunque ande en valle de sombra de muerte, no temeré mal alguno; porque tú estarás conmigo: tu vara y tu cayado me infundirán aliento.",
        "Aderezarás mesa delante de mí, en presencia de mis angustiadores: ungiste mi cabeza con aceite: mi copa está rebosando.",
        "Ciertamente el bien y la misericordia me seguirán todos los días de mi vida: y en la casa de Jehová moraré por largos días.",
    ];

    /// <summary>Real text for a few passages: (bookId, chapter) → (verse count, verse number → text).</summary>
    public static readonly Dictionary<(string Book, int Chapter), (int Count, Dictionary<int, string> Text)> KnownChapters = new()
    {
        [("sal", 23)] = (6, new()
        {
            [1] = Psalm23[0], [2] = Psalm23[1], [3] = Psalm23[2], [4] = Psalm23[3], [5] = Psalm23[4], [6] = Psalm23[5],
        }),
        [("jn", 1)] = (51, new()
        {
            [1] = "En el principio era el Verbo, y el Verbo era con Dios, y el Verbo era Dios.",
            [2] = "Este era en el principio con Dios.",
            [3] = "Todas las cosas por él fueron hechas; y sin él nada de lo que es hecho, fue hecho.",
            [4] = "En él estaba la vida, y la vida era la luz de los hombres.",
            [5] = "Y la luz en las tinieblas resplandece; mas las tinieblas no la comprendieron.",
        }),
        [("jn", 3)] = (36, new()
        {
            [16] = "Porque de tal manera amó Dios al mundo, que ha dado a su Hijo unigénito, para que todo aquel que en él cree, no se pierda, mas tenga vida eterna.",
            [17] = "Porque no envió Dios a su Hijo al mundo, para que condene al mundo, mas para que el mundo sea salvo por él.",
        }),
        [("gn", 1)] = (31, new()
        {
            [1] = "En el principio crió Dios los cielos y la tierra.",
            [2] = "Y la tierra estaba desordenada y vacía, y las tinieblas estaban sobre la haz del abismo, y el Espíritu de Dios se movía sobre la haz de las aguas.",
            [3] = "Y dijo Dios: Sea la luz: y fue la luz.",
        }),
    };

    public static IReadOnlyList<BibleBook> BibleBooks { get; } =
    [
        Old("gn", "Génesis", 50), Old("ex", "Éxodo", 40), Old("lv", "Levítico", 27), Old("nm", "Números", 36),
        Old("dt", "Deuteronomio", 34), Old("jos", "Josué", 24), Old("jue", "Jueces", 21), Old("rt", "Rut", 4),
        Old("1s", "1 Samuel", 31), Old("2s", "2 Samuel", 24), Old("1r", "1 Reyes", 22), Old("2r", "2 Reyes", 25),
        Old("1cr", "1 Crónicas", 29), Old("2cr", "2 Crónicas", 36), Old("esd", "Esdras", 10), Old("neh", "Nehemías", 13),
        Old("est", "Ester", 10), Old("job", "Job", 42), Old("sal", "Salmos", 150), Old("pr", "Proverbios", 31),
        Old("ec", "Eclesiastés", 12), Old("cnt", "Cantares", 8), Old("is", "Isaías", 66), Old("jer", "Jeremías", 52),
        Old("lm", "Lamentaciones", 5), Old("ez", "Ezequiel", 48), Old("dn", "Daniel", 12), Old("os", "Oseas", 14),
        Old("jl", "Joel", 3), Old("am", "Amós", 9), Old("abd", "Abdías", 1), Old("jon", "Jonás", 4),
        Old("mi", "Miqueas", 7), Old("nah", "Nahúm", 3), Old("hab", "Habacuc", 3), Old("sof", "Sofonías", 3),
        Old("hag", "Hageo", 2), Old("zac", "Zacarías", 14), Old("mal", "Malaquías", 4),
        New("mt", "Mateo", 28), New("mr", "Marcos", 16), New("lc", "Lucas", 24), New("jn", "Juan", 21),
        New("hch", "Hechos", 28), New("ro", "Romanos", 16), New("1co", "1 Corintios", 16), New("2co", "2 Corintios", 13),
        New("ga", "Gálatas", 6), New("ef", "Efesios", 6), New("fil", "Filipenses", 4), New("col", "Colosenses", 4),
        New("1ts", "1 Tesalonicenses", 5), New("2ts", "2 Tesalonicenses", 3), New("1ti", "1 Timoteo", 6), New("2ti", "2 Timoteo", 4),
        New("tit", "Tito", 3), New("flm", "Filemón", 1), New("heb", "Hebreos", 13), New("stg", "Santiago", 5),
        New("1p", "1 Pedro", 5), New("2p", "2 Pedro", 3), New("1jn", "1 Juan", 5), New("2jn", "2 Juan", 1),
        New("3jn", "3 Juan", 1), New("jud", "Judas", 1), New("ap", "Apocalipsis", 22),
    ];

    private static BibleBook Old(string id, string name, int chapters) => new(id, name, Testament.Old, chapters);

    private static BibleBook New(string id, string name, int chapters) => new(id, name, Testament.New, chapters);
}
