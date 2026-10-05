using System;
using System.Collections.Generic;
using System.Linq;
using Iris.Core.Models;

namespace Iris.Core.Services.Mocks;

/// <summary>Church demo data (IRIS_SPEC §11): 3 service types, 8 people, the last 10 Sundays of records.</summary>
public static class MockChurchData
{
    public static readonly Person DanielRuiz = new(Id(1, 1), "Daniel Ruiz");
    public static readonly Person AnaTorres = new(Id(1, 2), "Ana Torres");
    public static readonly Person CarlosPerez = new(Id(1, 3), "Carlos Pérez");
    public static readonly Person LuciaGomez = new(Id(1, 4), "Lucía Gómez");
    public static readonly Person MartaRivas = new(Id(1, 5), "Marta Rivas");
    public static readonly Person JoseHerrera = new(Id(1, 6), "José Herrera");
    public static readonly Person SofiaMendez = new(Id(1, 7), "Sofía Méndez");
    public static readonly Person PabloCastro = new(Id(1, 8), "Pablo Castro");

    public static readonly BlockTemplate Bienvenida = new(Id(3, 1), "Bienvenida", 10, CarlosPerez.Id);
    public static readonly BlockTemplate Alabanzas = new(Id(3, 2), "Alabanzas", 15, AnaTorres.Id);
    public static readonly BlockTemplate Predica = new(Id(3, 3), "Prédica", 40, DanielRuiz.Id);
    public static readonly BlockTemplate Anuncios = new(Id(3, 4), "Anuncios", 5, LuciaGomez.Id);

    public static readonly ServiceType CultoGeneral = new(Id(2, 1), "Culto general", "#FFB547", new ServiceSchedule(1, 10, 0), [Bienvenida, Alabanzas, Predica, Anuncios]);
    public static readonly ServiceType Jovenes = new(Id(2, 2), "Jóvenes", "#F0508C", new ServiceSchedule(7, 19, 0), []);
    public static readonly ServiceType Abc = new(Id(2, 3), "ABC", "#9B5CFF", new ServiceSchedule(1, 9, 0), []);

    public static ChurchModules Modules => ChurchModules.All;

    public static IReadOnlyList<ServiceType> ServiceTypes => [CultoGeneral, Jovenes, Abc];

    public static IReadOnlyList<Person> People => [DanielRuiz, AnaTorres, CarlosPerez, LuciaGomez, MartaRivas, JoseHerrera, SofiaMendez, PabloCastro];

    // Actual seconds per Sunday (1 = last week) for Bienvenida, Alabanzas, Prédica, Anuncios
    // (-1 = skipped) and who led each block.
    private static readonly (int[] Seconds, Person[] Leaders)[] Weeks =
    [
        ([580, 1145, 3090, 355], [CarlosPerez, AnaTorres, DanielRuiz, LuciaGomez]),
        ([615, 920, 2510, 290], [MartaRivas, AnaTorres, DanielRuiz, LuciaGomez]),
        ([560, 1010, 2790, 330], [CarlosPerez, SofiaMendez, JoseHerrera, LuciaGomez]),
        ([640, 880, 2350, 300], [CarlosPerez, AnaTorres, DanielRuiz, PabloCastro]),
        ([590, 960, 2600, -1], [CarlosPerez, AnaTorres, DanielRuiz, LuciaGomez]),
        ([600, 1080, 2400, 280], [MartaRivas, SofiaMendez, DanielRuiz, LuciaGomez]),
        ([545, 905, 2950, 310], [CarlosPerez, AnaTorres, JoseHerrera, LuciaGomez]),
        ([610, 1200, 2460, 270], [PabloCastro, AnaTorres, DanielRuiz, MartaRivas]),
        ([570, 940, 2880, 340], [CarlosPerez, SofiaMendez, DanielRuiz, LuciaGomez]),
        ([630, 870, 2380, 295], [CarlosPerez, AnaTorres, DanielRuiz, LuciaGomez]),
    ];

    /// <summary>The last 10 Sundays before <paramref name="today"/> at 10:00, most recent first.</summary>
    public static IReadOnlyList<ServiceRecord> Records(DateTime today)
    {
        var lastSunday = today.Date.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 1);
        var template = CultoGeneral.Blocks;
        return Weeks.Select((week, w) => new ServiceRecord(
            Id(4, w + 1),
            lastSunday.AddDays(-7 * w).AddHours(10),
            CultoGeneral.Id,
            template.Select((block, b) =>
            {
                var seconds = week.Seconds[b];
                var leader = week.Leaders[b];
                var status = seconds < 0 ? BlockStatus.Skipped
                    : w == 5 && block.Id == Predica.Id ? BlockStatus.Adjusted
                    : BlockStatus.Completed;
                return seconds < 0
                    ? new BlockRecord(block.Id, block.Name, block.PlannedMinutes * 60, 0, null, null, status)
                    : new BlockRecord(block.Id, block.Name, block.PlannedMinutes * 60, seconds, leader.Id, leader.Name, status);
            }).ToList())).ToList();
    }

    private static Guid Id(int kind, int n) => new($"{kind:00000000}-1415-4a5e-9a6b-{n:000000000000}");
}
