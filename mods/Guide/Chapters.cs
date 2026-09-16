using System;
using System.Collections.Generic;
using UnityEngine;
using static Guide.Need;
using static Heightmap.Biome;
using ModsCommon;

namespace Guide
{
    /// <summary>
    /// Le fil conducteur : un chapitre par boss, dans l'ordre normal du jeu. Chaque étape sait se vérifier toute seule
    /// avec ce que le jeu enregistre (voir Checks). Tout ce qui est chiffré vient du jeu, pas de mémoire : offrandes
    /// des autels (lues sur l'autel : objet et nombre), recettes et coûts de construction (affichés sous l'étape et
    /// lus dans ObjectDB / les prefabs). Vérifié contre l'export docs/guide_facts.txt.
    /// Obligatoire = sans ça le boss ne peut pas être invoqué ou le chapitre suivant est bloqué ;
    /// Conseillé = ce que fait un joueur prudent ; Optionnel = confort.
    /// </summary>
    internal static class Chapters
    {
        public static readonly List<Chapter> All = Build();

        private static Status AllOf(Checks c, params string[] prefabs)
        {
            int have = 0;
            foreach (var p in prefabs) if (c.Obtained(p) >= 1f) have++;
            return Status.Count(have, prefabs.Length);
        }
        /// <summary>Offrande : ce qu'on a SUR SOI maintenant (il faut les déposer à l'autel), pas les ramassages passés ni les kills.</summary>
        private static Status Have(Checks c, string prefab, int n) => Status.Count(c.InInventory(prefab), n);

        /// <summary>Étape « offrande » : objet et quantité lus sur l'autel du lieu (repli sur les valeurs données).</summary>
        private static Step OfferingStep(string id, string location, string fallbackItem, int fallbackCount, string detail, string finder, int multiplier = 1)
        {
            Facts.Offering Off() { var o = Facts.OfferingOf(location); return o.Valid ? o : new Facts.Offering { Item = fallbackItem, Count = fallbackCount, Valid = true }; }
            return new Step(id, Required, L.F("Rapporter {0} × {1}", fallbackCount * multiplier, fallbackItem), detail,
                c => { var o = Off(); return Have(c, o.Item, o.Count * multiplier); }, finder,
                titleFunc: () => { var o = Off(); return L.F("Rapporter {0} × {1}", o.Count * multiplier, Facts.ItemLabel(o.Item)); })
            { IconFunc = () => Off().Item, Volatile = true }; // état du moment : se décoche si on les dépose ou les perd
        }

        private static List<Chapter> Build()
        {
            var list = new List<Chapter>();

            list.Add(new Chapter
            {
                Id = "eikthyr", Title = "Invoquer Eikthyr", Boss = "Eikthyr", Icon = "TrophyEikthyr", Location = "Eikthyrnir", AltarLabel = "Autel d'Eikthyr", Finder = "Autel : Eikthyr",
                Intro = "Le premier boss, un cerf géant. Son autel est une pierre runique à quelques centaines de mètres des pierres sacrificielles ; il réclame des trophées de cerf.",
                Reward = "Ses bois durs (×3) permettent la pioche en bois de cerf (cuivre, étain). Accrochez son trophée aux pierres sacrificielles pour son pouvoir : moins d'endurance dépensée à courir et sauter.",
                Steps =
                {
                    new Step("hammer", Required, "Fabriquer un marteau", "Il sert à construire.", c => Status.Bool(c.Obtained("Hammer") >= 1), recipe: "Hammer"),
                    new Step("axe_stone", Required, "Fabriquer une hache de pierre", "Pour abattre les arbres.", c => Status.Bool(c.Obtained("AxeStone") >= 1), recipe: "AxeStone"),
                    new Step("workbench", Required, "Construire un établi", "Il débloque la fabrication et la construction ; il doit être sous un toit pour servir.", c => Status.Bool(c.Built("piece_workbench")), piece: "piece_workbench"),
                    new Step("bed", Advised, "Un abri et un lit", "Le lit fixe votre point d'apparition (et le retour F8). Il faut un feu à proximité pour dormir.", c => Status.Bool(c.Built("bed")), piece: "bed"),
                    new Step("altar", Required, "Trouver l'autel d'Eikthyr", "Pierre runique près du spawn. Le scanner peut vous y guider.", c => Status.Bool(c.LocationExplored("Eikthyrnir")), "Autel : Eikthyr"),
                    OfferingStep("deer", "Eikthyrnir", "TrophyDeer", 2, "Les cerfs en lâchent une fois sur deux. Arc ou lance, et de la patience.", "Cerf"),
                    new Step("bow", Advised, "Arc grossier et flèches de silex", "Restes de cuir des sangliers, silex au bord de l'eau. L'arc rend Eikthyr et les cerfs bien plus faciles.", c => AllOf(c, "Bow", "ArrowFlint"), "Silex", recipe: "Bow"),
                    new Step("leather", Optional, "Tenue de cuir complète", "Casque, tunique, pantalon : peaux de cerf, établi niveau 2. Un peu d'armure change tout.", c => AllOf(c, "HelmetLeather", "ArmorLeatherChest", "ArmorLeatherLegs"), recipe: "ArmorLeatherChest"),
                    new Step("food", Advised, "Manger deux aliments", "Avant le combat : viande cuite, baies, champignons, chaque aliment ajoute de la vie et de l'endurance.", c => Status.Count(c.Foods(), 2)) { Volatile = true },
                    new Step("kill", Required, "Déposer les trophées et vaincre Eikthyr", "Esquivez ses charges, frappez de côté.", c => Status.Bool(c.BossDefeated("Eikthyr"))),
                }
            });

            list.Add(new Chapter
            {
                Id = "elder", Title = "Invoquer L'Ancien", Boss = "gd_king", Icon = "TrophyTheElder", Location = "GDKing", AltarLabel = "Autel de L'Ancien", Finder = "Autel : L'Ancien",
                Intro = "Un arbre géant, quelque part dans la Forêt noire. Il faut brûler des graines anciennes sur son autel. L'âge du bronze commence.",
                Reward = "Sa clé de crypte (une par joueur) ouvre les cryptes des marais (fer). Pouvoir : coupe de bois plus rapide.",
                Steps =
                {
                    new Step("pickaxe", Required, "Fabriquer la pioche en bois de cerf", "Avec les bois durs d'Eikthyr.", c => Status.Bool(c.Obtained("PickaxeAntler") >= 1), recipe: "PickaxeAntler"),
                    new Step("biome", Required, "Explorer la Forêt noire", "Sapins et pins sombres, greydwarfs, trolls. Restez près de la lisière au début.", c => Status.Bool(c.BiomeKnown(BlackForest))),
                    new Step("copper", Required, "Miner du cuivre", "Gros rochers veinés de vert, souvent à flanc de colline. Le minerai ne passe pas les portails.", c => Status.Count(c.PickedUp("CopperOre"), 1), "Cuivre"),
                    new Step("tin", Required, "Miner de l'étain", "Petites pierres au bord de l'eau, dans la Forêt noire.", c => Status.Count(c.PickedUp("TinOre"), 1), "Étain"),
                    new Step("cores", Required, "Ramasser 10 noyaux de surtling", "Dans les chambres funéraires (5 pour la fonderie, 5 pour la charbonnière). « Cherchez-en dans les endroits sombres sous terre. »", c => Have(c, "SurtlingCore", 10), "Chambre funéraire"),
                    new Step("smelter", Required, "Construire une fonderie et une charbonnière", "La charbonnière fait le charbon de la fonderie.", c => Status.Count((c.Built("smelter") ? 1 : 0) + (c.Built("charcoal_kiln") ? 1 : 0), 2), piece: "smelter"),
                    new Step("forge", Required, "Construire une forge", "Elle fabrique tout ce qui est en métal.", c => Status.Bool(c.Built("forge")), piece: "forge"),
                    new Step("bronze", Required, "Fondre du bronze", "Cuivre et étain à la forge.", c => Status.Count(c.Obtained("Bronze"), 1), recipe: "Bronze"),
                    new Step("axe_bronze", Required, "Fabriquer une hache de bronze", "Indispensable : elle seule coupe les bouleaux et les chênes (bois de cœur).", c => Status.Bool(c.Obtained("AxeBronze") >= 1), recipe: "AxeBronze"),
                    new Step("finewood", Required, "Récolter du bois de cœur", "Bouleaux (écorce blanche) et chênes, à la hache de bronze.", c => Status.Count(c.PickedUp("FineWood"), 1), "Bouleau"),
                    OfferingStep("seeds", "GDKing", "AncientSeed", 3, "Lâchées par les greydwarfs brutes (une fois sur trois) et en détruisant leurs nids.", "Greydwarf (et nids)"),
                    new Step("altar", Required, "Trouver l'autel de l'Ancien", "Dans la Forêt noire, marqué par une pierre runique. Le scanner vous y guide.", c => Status.Bool(c.LocationExplored("GDKing")), "Autel : L'Ancien"),
                    new Step("bow_fine", Advised, "Arc de bois de cœur", "L'Ancien se combat surtout à distance ; les flèches de feu font mal à un arbre.", c => Status.Bool(c.Obtained("BowFineWood") >= 1), recipe: "BowFineWood"),
                    new Step("armor", Optional, "Armure de bronze ou de peau de troll", "Bronze : lourd et solide. Troll : léger et discret.", c => Status.Bool(c.Obtained("ArmorBronzeChest") >= 1 || c.Obtained("ArmorTrollLeatherChest") >= 1), recipe: "ArmorBronzeChest"),
                    new Step("mead", Optional, "Chaudron à hydromel et fermenteur", "Les bases d'hydromel se font au chaudron à hydromel, le fermenteur les transforme en potions (×6). Utiles dès les marais.", c => Status.Count((c.Built("piece_MeadCauldron") ? 1 : 0) + (c.Built("fermenter") ? 1 : 0), 2), piece: "piece_MeadCauldron"),
                    new Step("kill", Required, "Brûler les graines et vaincre L'Ancien", "Ses racines frappent au sol, ses projectiles au loin : utilisez les piliers de l'autel comme abri.", c => Status.Bool(c.BossDefeated("gd_king"))),
                }
            });

            list.Add(new Chapter
            {
                Id = "bonemass", Title = "Invoquer Bonemass", Boss = "Bonemass", Icon = "TrophyBonemass", Location = "Bonemass", AltarLabel = "Autel de Bonemass", Finder = "Autel : Bonemass",
                Intro = "Le boss des marais, un tas de vase et d'os. Son autel demande des os desséchés, qu'on trouve dans les cryptes englouties.",
                Reward = "Le bréchet (furcula) révèle l'argent et les trésors enfouis. Pouvoir : résistance aux dégâts physiques.",
                Steps =
                {
                    new Step("key", Required, "Récupérer la clé de crypte", "Lâchée par L'Ancien. Une seule clé suffit pour toutes les cryptes.", c => Status.Bool(c.Obtained("CryptKey") >= 1 || c.Known("CryptKey"))),
                    new Step("biome", Required, "Explorer les marais", "Sombres, pluvieux, pleins de draugrs et de sangsues. Évitez l'eau, sortez de jour.", c => Status.Bool(c.BiomeKnown(Swamp))),
                    new Step("crypt", Required, "Trouver une crypte engloutie", "Bâtiment de pierre avec des torches vertes. Le scanner les connaît toutes.", c => Status.Bool(c.LocationExplored("SunkenCrypt")), "Fer : cryptes des marais"),
                    new Step("scrap", Required, "Extraire de la ferraille", "Dans les cryptes, à la pioche, dans les tas de boue.", c => Status.Count(c.PickedUp("IronScrap"), 1)),
                    new Step("iron", Required, "Fondre du fer", "Ferraille à la fonderie. Le fer ne passe pas les portails.", c => Status.Count(c.Obtained("Iron"), 1)),
                    OfferingStep("bones", "Bonemass", "WitheredBone", 10, "Dans les tas de boue des cryptes et leurs coffres.", "Fer : cryptes des marais"),
                    new Step("altar", Required, "Trouver l'autel de Bonemass", "Un grand crâne dans les marais.", c => Status.Bool(c.LocationExplored("Bonemass")), "Autel : Bonemass"),
                    new Step("poison", Advised, "Hydromel anti-poison", "Bonemass empoisonne fort. Base au chaudron à hydromel (miel des ruches, chardon, queue de neck, charbon), puis fermenteur. Vraiment conseillé.", c => Status.Bool(c.Obtained("MeadPoisonResist") >= 1), recipe: "MeadBasePoisonResist"),
                    new Step("mace", Advised, "Une masse de fer", "Bonemass est faible au contondant et résiste aux tranchants et aux flèches.", c => Status.Bool(c.Obtained("MaceIron") >= 1), recipe: "MaceIron"),
                    new Step("armor", Optional, "Armure de fer", "Chère en fer mais solide pour la suite.", c => Status.Bool(c.Obtained("ArmorIronChest") >= 1), recipe: "ArmorIronChest"),
                    new Step("stonecutter", Optional, "Tailleur de pierre", "Constructions en pierre.", c => Status.Bool(c.Built("piece_stonecutter")), piece: "piece_stonecutter"),
                    new Step("food", Advised, "Trois bons aliments", "Saucisses, ragoût de navet, viande cuite…", c => Status.Count(c.Foods(), 3)) { Volatile = true },
                    new Step("kill", Required, "Déposer les os et vaincre Bonemass", "Long combat : reculez quand il vomit, frappez à la masse, gardez la potion active.", c => Status.Bool(c.BossDefeated("Bonemass"))),
                }
            });

            list.Add(new Chapter
            {
                Id = "moder", Title = "Invoquer Moder", Boss = "Dragon", Icon = "TrophyDragonQueen", Location = "Dragonqueen", AltarLabel = "Autel de Moder", Finder = "Autel : Moder",
                Intro = "La dragonne des montagnes. Des œufs de dragon à porter (lourds, un par un pour un joueur normal) jusqu'aux coupes de son autel.",
                Reward = "Ses larmes (×10) permettent la table d'artisan (moulin, rouet, haut fourneau…). Pouvoir : vent toujours favorable en bateau.",
                Steps =
                {
                    new Step("wishbone", Required, "Porter le bréchet", "Lâché par Bonemass (un par joueur). Il vibre près de l'argent et des trésors.", c => Status.Bool(c.Obtained("Wishbone") >= 1 || c.Known("Wishbone"))),
                    new Step("biome", Required, "Explorer la montagne", "Neige, loups, drakes. Sans protection, le froid vous tue.", c => Status.Bool(c.BiomeKnown(Mountain))),
                    new Step("frost", Required, "Hydromel anti-froid", "Base au chaudron à hydromel (miel, chardon, sacs de sang des sangsues, œil de greydwarf) puis fermenteur. La cape de loup viendra après, elle demande de l'argent.", c => Status.Bool(c.Obtained("MeadFrostResist") >= 1 || c.Obtained("CapeWolf") >= 1), recipe: "MeadBaseFrostResist"),
                    new Step("silver", Required, "Miner de l'argent", "Veines enfouies : le bréchet guide, la pioche de fer creuse.", c => Status.Count(c.PickedUp("SilverOre"), 1), "Argent"),
                    OfferingStep("eggs", "Dragonqueen", "DragonEgg", 3, "Dans les nids des drakes, en montagne. Chaque œuf pèse 200 (vous n'avez pas de limite de poids).", "Œuf de dragon"),
                    new Step("altar", Required, "Trouver l'autel de Moder", "Au sommet d'une montagne : trois coupes pour les œufs.", c => Status.Bool(c.LocationExplored("Dragonqueen")), "Autel : Moder"),
                    new Step("bow", Advised, "Arc croc de draugr", "Moder vole : un bon arc et des flèches d'obsidienne.", c => Status.Bool(c.Obtained("BowDraugrFang") >= 1), recipe: "BowDraugrFang"),
                    new Step("armor", Optional, "Armure de loup", "Chaude et solide : plus besoin d'hydromel.", c => Status.Bool(c.Obtained("ArmorWolfChest") >= 1), recipe: "ArmorWolfChest"),
                    new Step("kill", Required, "Déposer les œufs et vaincre Moder", "Au sol elle mord et souffle la glace ; en vol, flèches. Abritez-vous des cristaux.", c => Status.Bool(c.BossDefeated("Dragon"))),
                }
            });

            list.Add(new Chapter
            {
                Id = "yagluth", Title = "Invoquer Yagluth", Boss = "GoblinKing", Icon = "TrophyGoblinKing", Location = "GoblinKing", AltarLabel = "Autel de Yagluth", Finder = "Autel : Yagluth",
                Intro = "Le roi fuling des plaines. Des totems fuling, pris dans leurs villages, à poser sur les supports de son autel.",
                Reward = "Pouvoir : résistance au feu, au froid et à la foudre. Ses restes (×3) ouvrent les Brumes.",
                Steps =
                {
                    new Step("artisan", Required, "Table d'artisan", "Nécessaire pour le moulin, le rouet et le haut fourneau.", c => Status.Bool(c.Built("piece_artisanstation")), piece: "piece_artisanstation"),
                    new Step("biome", Required, "Explorer les plaines", "Herbes hautes, moustiques mortels, fulings. Bouclier obligatoire.", c => Status.Bool(c.BiomeKnown(Plains))),
                    new Step("camp", Required, "Trouver un village fuling", "Palissades et totems. Approchez de nuit ou par l'arc, un par un.", c => Status.Bool(c.LocationExplored("GoblinCamp")), "Village fuling"),
                    OfferingStep("totems", "GoblinKing", "GoblinTotem", 5, "Sur les mâts-totems des villages (rarement sur les berserkers).", "Village fuling"),
                    new Step("altar", Required, "Trouver l'autel de Yagluth", "Un cercle de pierres dans les plaines.", c => Status.Bool(c.LocationExplored("GoblinKing")), "Autel : Yagluth"),
                    new Step("blackmetal", Advised, "Haut fourneau et métal noir", "Ferraille noire des fulings, fondue au haut fourneau.", c => Status.Count((c.Built("blastfurnace") ? 1 : 0) + (c.Obtained("BlackMetal") >= 1 ? 1 : 0), 2), piece: "blastfurnace"),
                    new Step("linen", Advised, "Rouet et fil de lin", "Le lin sauvage pousse dans les plaines ; le rouet en fait du fil, indispensable aux armes en métal noir et à l'armure matelassée.", c => Status.Count((c.Built("piece_spinningwheel") ? 1 : 0) + (c.Obtained("LinenThread") >= 1 ? 1 : 0), 2), "Lin sauvage", piece: "piece_spinningwheel"),
                    new Step("weapon", Advised, "Une arme en métal noir", "Épée, hache, couteau ou hallebarde (forge niveau 4).", c => Status.Bool(c.Obtained("SwordBlackmetal") >= 1 || c.Obtained("AxeBlackMetal") >= 1 || c.Obtained("KnifeBlackMetal") >= 1 || c.Obtained("AtgeirBlackmetal") >= 1), recipe: "SwordBlackmetal"),
                    new Step("wine", Advised, "Vin d'orge anti-feu", "Yagluth crache des météores. Orge des plaines et mûres arctiques (montagne) au chaudron à hydromel, puis fermenteur.", c => Status.Bool(c.Obtained("BarleyWine") >= 1), "Orge sauvage", recipe: "BarleyWineBase"),
                    new Step("armor", Optional, "Armure matelassée ou cape de lox", "Le meilleur de l'âge du fer noir.", c => Status.Bool(c.Obtained("ArmorPaddedCuirass") >= 1 || c.Obtained("CapeLox") >= 1), recipe: "ArmorPaddedCuirass"),
                    new Step("kill", Required, "Poser les totems et vaincre Yagluth", "Restez mobile, hors des météores et du rayon ; frappez entre ses attaques.", c => Status.Bool(c.BossDefeated("GoblinKing"))),
                }
            });

            list.Add(new Chapter
            {
                Id = "queen", Title = "Invoquer La Reine", Boss = "SeekerQueen", Icon = "TrophySeekerQueen", Location = "Mistlands_DvergrBossEntrance", AltarLabel = "Antre de La Reine", Finder = "Autel : La Reine",
                Intro = "La reine des seekers, derrière une porte scellée des Brumes. Il faut forger un brise-sceau avec neuf fragments tirés des mines infestées, et pour ça, toute la chaîne de l'eitr.",
                Reward = "Pouvoir : régénération d'eitr. Ses restes (×5) ouvrent les Ashlands.",
                Steps =
                {
                    new Step("biome", Required, "Explorer les Brumes", "Brouillard, falaises, seekers. On n'y voit rien sans lumière follet.", c => Status.Bool(c.BiomeKnown(Mistlands))),
                    new Step("wisp", Required, "Lampe follet", "Feux follets (la nuit, dans les Brumes) + argent, à l'établi. Elle dissipe la brume.", c => Status.Bool(c.Obtained("Demister") >= 1), recipe: "Demister"),
                    new Step("mine", Required, "Trouver une mine infestée", "Tours dvergr et mines dans les Brumes : fragments de brise-sceau, noyaux noirs.", c => Status.Bool(c.LocationExplored("Mistlands_DvergrTownEntrance")), "Cité dvergr (Mistlands)"),
                    new Step("fragments", Required, "Rassembler 9 fragments de brise-sceau", "Dans les mines infestées, gardés par les seekers.", c => Have(c, "DvergrKeyFragment", 9), "Cité dvergr (Mistlands)"),
                    new Step("sap", Required, "Extracteur de sève", "Sur une racine d'Yggdrasil. Aiguille dvergr dans leurs caisses, bois d'Yggdrasil des pousses.", c => Status.Bool(c.Built("piece_sapcollector")), piece: "piece_sapcollector"),
                    new Step("refinery", Required, "Raffinerie d'eitr", "Marbre noir (structures dvergr, ossements pétrifiés), noyaux noirs (mines), sève.", c => Status.Bool(c.Built("eitrrefinery")), piece: "eitrrefinery"),
                    new Step("eitr", Required, "Raffiner de l'eitr", "Tissu mou (dvergr, ossements de géants) à la raffinerie.", c => Status.Count(c.Obtained("Eitr"), 1)),
                    new Step("magetable", Required, "Table de galdr", "C'est là que se forge le brise-sceau (et les bâtons de magie).", c => Status.Bool(c.Built("piece_magetable")), piece: "piece_magetable"),
                    new Step("key", Required, "Forger le brise-sceau", "À la table de galdr.", c => Status.Bool(c.Obtained("DvergrKey") >= 1), recipe: "DvergrKey"),
                    new Step("altar", Required, "Trouver la porte de la Reine", "Une porte scellée dans les Brumes, que seul le brise-sceau ouvre.", c => Status.Bool(c.LocationExplored("Mistlands_DvergrBossEntrance")), "Autel : La Reine"),
                    new Step("blackforge", Advised, "Forge noire et arme des Brumes", "Mistwalker : épée de givre.", c => Status.Count((c.Built("blackforge") ? 1 : 0) + (c.Obtained("SwordMistwalker") >= 1 ? 1 : 0), 2), piece: "blackforge"),
                    new Step("armor", Optional, "Armure de carapace", "Carapaces de seekers à la forge noire.", c => Status.Bool(c.Obtained("ArmorCarapaceChest") >= 1), recipe: "ArmorCarapaceChest"),
                    new Step("kill", Required, "Ouvrir la porte et vaincre La Reine", "Elle creuse, appelle des seekers, crache de l'acide : combat en espace clos, prenez des potions.", c => Status.Bool(c.BossDefeated("SeekerQueen"))),
                }
            });

            list.Add(new Chapter
            {
                Id = "fader", Title = "Invoquer Fader", Boss = "Fader", Icon = "TrophyFader", Location = "FaderLocation", AltarLabel = "Autel de Fader", Finder = "Autel : Fader",
                Intro = "Le seigneur des Ashlands, terre de feu au sud. Il faut forger des cloches avec les fragments pris dans les forteresses calcinées et les accrocher à son autel.",
                Reward = "Pouvoir de Fader. Ses restes (×5). Il ne reste que le Nord profond.",
                Steps =
                {
                    new Step("ship", Required, "Un drakkar", "« Pour traverser ces eaux bouillonnantes, vous devrez construire quelque chose de plus solide. » Plaques de céramique : marbre noir à la table d'artisan niveau 2.", c => Status.Bool(c.Placed("VikingShip_Ashlands") > 0f), piece: "VikingShip_Ashlands"),
                    new Step("fire", Advised, "Vin d'orge anti-feu", "Tout brûle là-bas.", c => Status.Bool(c.Obtained("BarleyWine") >= 1), recipe: "BarleyWineBase"),
                    new Step("biome", Required, "Explorer les Ashlands", "Plein sud, en drakkar. Lave, calcinés, morgens.", c => Status.Bool(c.BiomeKnown(AshLands))),
                    new Step("fortress", Required, "Trouver une forteresse calcinée", "Grands murs noirs. Chacune garde un fragment de cloche sur son autel.", c => Status.Bool(c.LocationExplored("CharredFortress")), "Forteresse calcinée"),
                    OfferingStep("fragments", "FaderLocation", "Bell", 3, "Un fragment par autel de forteresse ; trois fragments par cloche (forge noire).", "Forteresse calcinée", multiplier: 3),
                    OfferingStep("bells", "FaderLocation", "Bell", 3, "À la forge noire, avec les fragments.", null),
                    new Step("altar", Required, "Trouver l'autel de Fader", "Les porte-cloches, au cœur des Ashlands.", c => Status.Bool(c.LocationExplored("FaderLocation")), "Autel : Fader"),
                    new Step("flametal", Optional, "Flametal", "Se mine sur le dos des léviathans de lave (lieu LeviathanLava) : vite, ils s'enfoncent.", c => Status.Count(c.PickedUp("FlametalOreNew"), 1), "Flametal (léviathan de lave)"),
                    new Step("kill", Required, "Accrocher les cloches et vaincre Fader", "Météores, murs de feu, souffle : vin anti-feu actif, jamais immobile.", c => Status.Bool(c.BossDefeated("Fader"))),
                }
            });

            list.Add(new Chapter
            {
                Id = "frozenking", Title = "Invoquer le Roi gelé", TitleFunc = () => "Invoquer " + Facts.Loc("$enemy_frozenking"), Boss = "FrozenKing", Icon = "TrophyBjorn", Location = "DN_Bossroom", AltarLabel = "Salle du boss du Nord profond", Finder = "Boss du Nord profond",
                Intro = "Le dernier boss (Nord profond, Valheim 1.0). Son antre est fermé : il faut du sang haineux, tiré des noyaux de glace noire que laissent les invasions de jötnars, « Mettez fin à l'invasion ».",
                Reward = "« Vous avez trouvé le sang sacrificiel, la dernière clé pour restaurer ce royaume… retournez là où tout a commencé. »",
                Steps =
                {
                    new Step("frost", Required, "Résistance au froid", "Hydromel anti-froid ou cape de loup, en permanence.", c => Status.Bool(c.Obtained("MeadFrostResist") >= 1 || c.Obtained("CapeWolf") >= 1), recipe: "MeadBaseFrostResist"),
                    new Step("biome", Required, "Explorer le Nord profond", "Tout au nord, en bateau. Blizzards, glace, jötnars.", c => Status.Bool(c.BiomeKnown(DeepNorth))),
                    new Step("invasion", Required, "Mettre fin aux invasions de jötnars", "L'événement « Des jötnars vous envahissent » installe de la glace noire dans vos terres (prairies, forêt, marais, montagne, plaines). Détruisez le noyau de glace noire : il laisse un sang haineux.", c => Have(c, "HatefulBlood", 1)),
                    new Step("blood", Required, "Rapporter 6 × sang haineux", "3 pour ouvrir la porte de l'antre, 3 pour l'autel du roi.", c => { var o = Facts.OfferingOf("DN_Bossroom"); int n = o.Valid ? o.Count * 2 : 6; return Have(c, o.Valid ? o.Item : "HatefulBlood", n); },
                        titleFunc: () => { var o = Facts.OfferingOf("DN_Bossroom"); return o.Valid ? L.F("Rapporter {0} × {1}", o.Count * 2, Facts.ItemLabel(o.Item)) : "Rapporter 6 × sang haineux"; }),
                    new Step("lair", Required, "Trouver l'antre du roi", "Le scanner connaît son emplacement.", c => Status.Bool(c.LocationExplored("DN_Bossroom")), "Boss du Nord profond"),
                    new Step("morkhalla", Optional, "Morkhalla, la forteresse des jötnars", "Sa porte demande une clé d'or sanglant : lâchée par les sorcières jötnars (rare) ou forgée (or + moule de clés, forge noire, puis fonderie de givre).", c => Status.Bool(c.LocationExplored("MorkBorg")), "Or (trolls pétrifiés)"),
                    new Step("frostwood", Optional, "Bois de givre et noyaux de givre", "Nouveaux matériaux du Nord : pins enneigés, fryslings.", c => Status.Count((c.PickedUp("Frostwood") >= 1 ? 1 : 0) + (c.PickedUp("FrostCore") >= 1 ? 1 : 0), 2)),
                    new Step("kill", Required, "Ouvrir la porte, verser le sang et vaincre le roi", "Trois phases ; il rappelle les boss précédents.", c => Status.Bool(c.BossDefeated("FrozenKing"))),
                }
            });

            return list;
        }
    }
}
