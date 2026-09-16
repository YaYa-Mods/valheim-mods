using System;
using ModsCommon;
using System.Collections.Generic;
using static Heightmap.Biome;

namespace ResourceFinder
{
    internal enum Category { Minerai, Cueillette, Arbre, Lieu, Creature }

    /// <summary>
    /// Une ressource cherchable : un libellé, des noms de prefabs (objets du monde, comparés sans casse)
    /// et/ou des motifs de lieux (sous-chaîne du nom du lieu : cryptes, autels, marchand...).
    /// Noms vérifiés contre l'export du jeu (ResourceFinder.names.txt) et, pour les objets placés par la génération,
    /// contre la table de végétation exportée (ResourceFinder.vegetation.txt) : ce sont les prefabs réellement posés.
    /// <c>Biomes</c> : indice de biome pour le scan fantôme quand l'objet n'est pas dans la table de végétation
    /// (placé par un lieu : cryptes, nids, mares de goudron, Nord profond...).
    /// Créatures : les prefabs sont ceux des créatures (elles bougent, on ne trouve que celles qui existent dans le
    /// monde) et, quand il y en a, de leurs nids (statiques, trouvés aussi par le scan).
    /// </summary>
    internal sealed class ResourceEntry
    {
        public string Label;
        public Category Category = Category.Minerai;
        public string[] Prefabs = Array.Empty<string>();
        public string[] Locations = Array.Empty<string>();
        /// <summary>Prefab d'item dont on affiche l'icône (sinon : butin du premier prefab résolu).</summary>
        public string Icon;
        public Heightmap.Biome Biomes = None;
        /// <summary>Les prefabs sont des objets de surface placés dans les lieux listés : un lieu dont la zone est chargée et qui n'en contient plus a été récolté (épingle retirée).</summary>
        public bool SurfacePrefabs;

        public ResourceEntry(string label, string[] prefabs = null, string[] locations = null, string icon = null, Heightmap.Biome biomes = None)
        {
            Label = label;
            if (prefabs != null) Prefabs = prefabs;
            if (locations != null) Locations = locations;
            Icon = icon;
            Biomes = biomes;
        }
    }

    internal static class Catalog
    {
        public static readonly List<ResourceEntry> Entries = Build();

        public static string CategoryLabel(Category c)
        {
            switch (c)
            {
                case Category.Minerai: return L.T("Minerais");
                case Category.Cueillette: return L.T("Cueillette");
                case Category.Arbre: return L.T("Arbres");
                case Category.Lieu: return L.T("Lieux");
                default: return L.T("Créatures");
            }
        }

        private static List<ResourceEntry> Build()
        {
            var list = new List<ResourceEntry>();
            void Add(Category cat, params ResourceEntry[] entries) { foreach (var e in entries) { e.Category = cat; list.Add(e); } }

            Add(Category.Minerai,   // ne repoussent pas
                new ResourceEntry("Cuivre",                 new[] { "rock4_copper", "MineRock_Copper" }),
                new ResourceEntry("Étain",                  new[] { "MineRock_Tin" }),
                new ResourceEntry("Fer : cryptes des marais", locations: new[] { "SunkenCrypt" }, icon: "IronScrap"),
                new ResourceEntry("Fer : tas de ferraille", new[] { "mudpile_beacon", "mudpile", "mudpile2" }, biomes: Swamp),
                new ResourceEntry("Argent",                 new[] { "silvervein", "rock3_silver" }),
                new ResourceEntry("Obsidienne",             new[] { "MineRock_Obsidian" }, biomes: Mountain),
                // Audit du jeu (guide_facts.txt) : « goldvein » n'est placé nulle part ; l'or du monde vient des trolls pétrifiés
                // (TrollFrost_Frac) des lieux DN_gammeltrollFrac01/02 du Nord profond (×30 chacun).
                new ResourceEntry("Or (trolls pétrifiés)",  new[] { "TrollFrost_Frac", "goldvein", "goldvein_frac" }, locations: new[] { "DN_gammeltrollFrac" }, icon: "GoldOre", biomes: DeepNorth) { SurfacePrefabs = true },
                // Audit du jeu : MineRock_Meteorite / FlametalRockstand ne sont placés nulle part (les « rockstand » donnent du charbon) ;
                // le minerai de flametal (FlametalOreNew) se mine sur le léviathan de lave (lieu LeviathanLava, Ashlands).
                new ResourceEntry("Flametal (léviathan de lave)", new[] { "LeviathanLava" }, locations: new[] { "LeviathanLava" }, icon: "FlametalOreNew", biomes: AshLands) { SurfacePrefabs = true },
                new ResourceEntry("Soufre / pierre de cendre", new[] { "Pickable_SulfurRock", "Pickable_Ashstone" }, locations: new[] { "SulfurArch" }, biomes: AshLands) { SurfacePrefabs = true });

            Add(Category.Cueillette, // repousse
                new ResourceEntry("Framboises",             new[] { "RaspberryBush" }),
                new ResourceEntry("Myrtilles",              new[] { "BlueberryBush" }),
                new ResourceEntry("Mûres arctiques",        new[] { "CloudberryBush" }),
                new ResourceEntry("Airelles (Nord profond)", new[] { "LingonberryBush" }),
                new ResourceEntry("Champignons",            new[] { "Pickable_Mushroom", "Pickable_Mushroom_yellow", "Pickable_Mushroom_JotunPuffs", "Pickable_Mushroom_Magecap" }),
                new ResourceEntry("Chardon",                new[] { "Pickable_Thistle" }),
                new ResourceEntry("Pissenlit",              new[] { "Pickable_Dandelion" }),
                new ResourceEntry("Lin sauvage",            new[] { "Pickable_Flax_Wild" }, locations: new[] { "GoblinCamp" }, biomes: Plains) { SurfacePrefabs = true }, // champs des villages fulings
                new ResourceEntry("Orge sauvage",           new[] { "Pickable_Barley_Wild" }, locations: new[] { "GoblinCamp" }, biomes: Plains) { SurfacePrefabs = true }, // champs des villages fulings
                // « Avoine » et « Graines d'oignon » retirées : Pickable_Oat / Pickable_SeedOnion ne sont placés nulle part (graines en coffres / marchand).
                new ResourceEntry("Chou frisé (kale)",      new[] { "Pickable_SeedKale" }, biomes: DeepNorth),
                new ResourceEntry("Fougère (fiddlehead)",   new[] { "Pickable_Fiddlehead" }, locations: new[] { "CharredTowerRuins" }, biomes: AshLands), // ruines calcinées (audit)
                new ResourceEntry("Gelée royale",           new[] { "Pickable_RoyalJelly" }, locations: new[] { "Mistlands_DvergrTownEntrance" }, biomes: Mistlands), // salles des cités dvergr
                new ResourceEntry("Graines de carotte",     new[] { "Pickable_SeedCarrot" }),
                new ResourceEntry("Graines de navet",       new[] { "Pickable_SeedTurnip" }),
                new ResourceEntry("Silex",                  new[] { "Pickable_Flint" }),
                new ResourceEntry("Ruche",                  new[] { "Beehive" }, biomes: Meadows | BlackForest),
                new ResourceEntry("Goudron",                new[] { "TarLiquid", "Pickable_Tar", "Pickable_TarBig" }, locations: new[] { "TarPit" }, biomes: Plains) { SurfacePrefabs = true },
                new ResourceEntry("Œuf de dragon",          new[] { "Pickable_DragonEgg" }, locations: new[] { "DrakeNest" }, biomes: Mountain) { SurfacePrefabs = true },
                new ResourceEntry("Œuf de vautour",         new[] { "Pickable_VoltureEgg" }, locations: new[] { "VoltureNest" }, biomes: AshLands) { SurfacePrefabs = true },
                new ResourceEntry("Cristal (grottes)",      new[] { "Pickable_MountainCaveCrystal" }, locations: new[] { "MountainCave" }, biomes: Mountain));

            Add(Category.Arbre,     // ne repoussent pas
                new ResourceEntry("Chêne",                  new[] { "Oak1" }, icon: "Acorn"),
                new ResourceEntry("Hêtre",                  new[] { "Beech1", "Beech_small1", "Beech_small2" }, icon: "BeechSeeds"),
                new ResourceEntry("Bouleau",                new[] { "Birch1", "Birch2", "Birch1_aut", "Birch2_aut" }, icon: "BirchSeeds"),
                new ResourceEntry("Sapin",                  new[] { "FirTree", "FirTree_small", "SnowFirTree", "SnowFirTree_small" }, icon: "FirCone"), // « SnowFirTree 2 » : le jeu coupe le nom à l'espace → même hachage que SnowFirTree
                new ResourceEntry("Pin",                    new[] { "Pinetree_01", "Pinetree_Snow", "Pinetree_Snow_dead" }, icon: "PineCone"),
                new ResourceEntry("Arbre ancien",           new[] { "SwampTree1" }, icon: "ElderBark"),
                new ResourceEntry("Pousse d'Yggdrasil",     new[] { "YggaShoot1", "YggaShoot2", "YggaShoot3", "YggaShoot_small1" }, icon: "YggdrasilWood"),
                new ResourceEntry("Arbre des Ashlands",     new[] { "AshlandsTree1", "AshlandsTree3", "AshlandsTree6" }, icon: "Blackwood"));

            Add(Category.Lieu,      // connus pour toute la carte
                new ResourceEntry("Grotte de troll",        locations: new[] { "TrollCave" }, icon: "TrollHide"),
                new ResourceEntry("Village fuling",         locations: new[] { "GoblinCamp" }, icon: "GoblinTotem"),
                new ResourceEntry("Chambre funéraire",      locations: new[] { "Crypt2", "Crypt3", "Crypt4" }, icon: "SurtlingCore"),
                new ResourceEntry("Grotte de montagne",     locations: new[] { "MountainCave" }, icon: "TrophyFenring"),
                new ResourceEntry("Cité dvergr (Mistlands)", locations: new[] { "Mistlands_DvergrTownEntrance" }, icon: "TrophyDvergr"),
                new ResourceEntry("Forteresse calcinée",    locations: new[] { "CharredFortress" }, icon: "TrophyCharredMelee"),
                new ResourceEntry("Marchand (Haldor)",      locations: new[] { "Vendor_BlackForest" }, icon: "Coins"),
                new ResourceEntry("Hildir",                 locations: new[] { "Hildir_camp" }, icon: "Coins"),
                new ResourceEntry("Sorcière des marais",    locations: new[] { "BogWitch_Camp" }, icon: "Coins"),
                new ResourceEntry("Autel : Eikthyr",        locations: new[] { "Eikthyrnir" }, icon: "TrophyEikthyr"),
                new ResourceEntry("Autel : L'Ancien",       locations: new[] { "GDKing" }, icon: "TrophyTheElder"),
                new ResourceEntry("Autel : Bonemass",       locations: new[] { "Bonemass" }, icon: "TrophyBonemass"),
                new ResourceEntry("Autel : Moder",          locations: new[] { "Dragonqueen" }, icon: "TrophyDragonQueen"),
                new ResourceEntry("Autel : Yagluth",        locations: new[] { "GoblinKing" }, icon: "TrophyGoblinKing"),
                new ResourceEntry("Autel : La Reine",       locations: new[] { "Mistlands_DvergrBossEntrance" }, icon: "TrophySeekerQueen"),
                new ResourceEntry("Autel : Fader",          locations: new[] { "FaderLocation" }, icon: "TrophyFader"),
                new ResourceEntry("Boss du Nord profond",   locations: new[] { "DN_Bossroom" }, icon: "TrophyBjorn"));

            Add(Category.Creature,  // trouvées seulement si présentes dans le monde ; nids en plus quand il y en a
                // Prairies
                new ResourceEntry("Sanglier",               new[] { "Boar" }, icon: "TrophyBoar"),
                new ResourceEntry("Cerf",                   new[] { "Deer", "Deer_White" }, icon: "TrophyDeer"),
                new ResourceEntry("Neck",                   new[] { "Neck" }, icon: "TrophyNeck"),
                new ResourceEntry("Greyling",               new[] { "Greyling" }, icon: "Resin"),
                // Forêt noire
                new ResourceEntry("Greydwarf (et nids)",    new[] { "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman", "Spawner_GreydwarfNest" }, icon: "TrophyGreydwarf", biomes: BlackForest),
                new ResourceEntry("Troll",                  new[] { "Troll" }, icon: "TrophyFrostTroll"),
                new ResourceEntry("Squelette (et ossuaires)", new[] { "Skeleton", "Skeleton_Poison", "BonePileSpawner" }, icon: "TrophySkeleton", biomes: BlackForest),
                new ResourceEntry("Fantôme",                new[] { "Ghost" }, icon: "TrophyGhost"),
                // Marais
                new ResourceEntry("Draugr (et tas d'os)",   new[] { "Draugr", "Draugr_Elite", "Draugr_Ranged", "Spawner_DraugrPile" }, icon: "TrophyDraugr", biomes: Swamp),
                new ResourceEntry("Sangsue",                new[] { "Leech" }, icon: "TrophyLeech"),
                new ResourceEntry("Blob / Oozer",           new[] { "Blob", "BlobElite" }, icon: "TrophyBlob"),
                new ResourceEntry("Spectre",                new[] { "Wraith" }, icon: "TrophyWraith"),
                new ResourceEntry("Surtling",               new[] { "Surtling" }, icon: "TrophySurtling"),
                new ResourceEntry("Abomination",            new[] { "Abomination" }, icon: "TrophyAbomination"),
                // Montagne
                new ResourceEntry("Loup",                   new[] { "Wolf" }, icon: "TrophyWolf"),
                new ResourceEntry("Drake",                  new[] { "Hatchling" }, icon: "TrophyHatchling"),
                new ResourceEntry("Fenring / Cultiste",     new[] { "Fenring", "Fenring_Cultist" }, icon: "TrophyFenring"),
                new ResourceEntry("Golem de pierre",        new[] { "StoneGolem" }, icon: "TrophySGolem"),
                new ResourceEntry("Ulv",                    new[] { "Ulv" }, icon: "TrophyUlv"),
                // Plaines
                new ResourceEntry("Fuling",                 new[] { "Goblin", "GoblinArcher", "GoblinShaman" }, icon: "TrophyGoblin"),
                new ResourceEntry("Berserker fuling",       new[] { "GoblinBrute" }, icon: "TrophyGoblinBrute"),
                new ResourceEntry("Deathsquito",            new[] { "Deathsquito" }, icon: "TrophyDeathsquito"),
                new ResourceEntry("Lox",                    new[] { "Lox" }, icon: "TrophyLox"),
                new ResourceEntry("Growth",                 new[] { "BlobTar" }, icon: "TrophyGrowth"),
                // Océan
                new ResourceEntry("Serpent",                new[] { "Serpent" }, icon: "TrophySerpent"),
                // Brumes
                new ResourceEntry("Seeker",                 new[] { "Seeker", "SeekerBrood" }, icon: "TrophySeeker"),
                new ResourceEntry("Soldat seeker",          new[] { "SeekerBrute" }, icon: "TrophySeekerBrute"),
                new ResourceEntry("Tique",                  new[] { "Tick" }, icon: "TrophyTick"),
                new ResourceEntry("Gjall",                  new[] { "Gjall" }, icon: "TrophyGjall"),
                new ResourceEntry("Dvergr",                 new[] { "Dverger", "DvergerMage" }, icon: "TrophyDvergr"),
                new ResourceEntry("Lièvre",                 new[] { "Hare" }, icon: "TrophyHare"),
                // Ashlands
                new ResourceEntry("Calcinés",               new[] { "Charred_Melee", "Charred_Archer", "Charred_Mage", "Charred_Twitcher" }, icon: "TrophyCharredMelee"),
                new ResourceEntry("Morgen",                 new[] { "Morgen" }, icon: "TrophyMorgen"),
                new ResourceEntry("Valkyrie déchue",        new[] { "FallenValkyrie" }, icon: "TrophyFallenValkyrie"),
                new ResourceEntry("Vautour",                new[] { "Volture" }, icon: "TrophyVolture"),
                new ResourceEntry("Asksvin",                new[] { "Asksvin" }, icon: "TrophyAsksvin"),
                new ResourceEntry("Bonemaw",                new[] { "BonemawSerpent" }, icon: "TrophyBonemawSerpent"),
                new ResourceEntry("Blob de lave",           new[] { "BlobLava" }, icon: "TrophyBlob_Lava"),
                // Nord profond
                new ResourceEntry("Élan",                   new[] { "Moose" }, icon: "TrophyMoose"),
                new ResourceEntry("Phoque",                 new[] { "Seal" }, icon: "TrophySeal"),
                new ResourceEntry("Barka",                  new[] { "Barka" }, icon: "TrophyBarka"),
                new ResourceEntry("Writhan",                new[] { "Writhan" }, icon: "TrophyWrithan"),
                new ResourceEntry("Jötunn",                 new[] { "JotunWarrior", "JotunWarriorDualWield", "JotunWitch" }, icon: "TrophyJotunWarrior"),
                new ResourceEntry("Elaking",                new[] { "Elaking" }, icon: "TrophyElaking"),
                new ResourceEntry("Blob de givre",          new[] { "BlobFrost" }, icon: "TrophyBlob_Frost"));

            return list;
        }
    }
}
