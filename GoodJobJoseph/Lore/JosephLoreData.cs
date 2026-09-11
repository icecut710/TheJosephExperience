using System;

namespace JosephExperience.Lore;

/// <summary>Static, deterministic lore database (part 1).</summary>
public static class JosephLoreData
{
    public static readonly LoreEntry[] All = BuildAll();

    private static void Add(List<LoreEntry> list, LoreEntry e) => list.Add(e);
    private static LoreEntry C(string id, string text, LoreCategory cat, LoreTrigger trig = LoreTrigger.None, string game = "", string evt = "") =>
        new LoreEntry(id, text, cat, LoreRarity.Common, null, 30, 100, trig, game, evt);
    private static LoreEntry U(string id, string text, LoreCategory cat, LoreTrigger trig = LoreTrigger.None, string game = "", string evt = "") =>
        new LoreEntry(id, text, cat, LoreRarity.Uncommon, null, 60, 35, trig, game, evt);
    private static LoreEntry R(string id, string text, LoreCategory cat, LoreTrigger trig = LoreTrigger.None, string game = "", string evt = "") =>
        new LoreEntry(id, text, cat, LoreRarity.Rare, null, 180, 8, trig, game, evt);
    private static LoreEntry Lg(string id, string text, LoreCategory cat, LoreTrigger trig = LoreTrigger.None, string game = "", string evt = "") =>
        new LoreEntry(id, text, cat, LoreRarity.Legendary, null, 600, 1, trig, game, evt);
    private static LoreEntry Ctx(string id, string text, LoreCategory cat, LoreTrigger trig, string game = "", string evt = "") =>
        new LoreEntry(id, text, cat, LoreRarity.Common, null, 30, 100, trig, game, evt);
    private static LoreEntry M(string id, string text, int min, int max) =>
        new LoreEntry(id, text, LoreCategory.Milestone, LoreRarity.Uncommon, null, 3600, 1, LoreTrigger.Milestone, "", "", min, max, false, false, false, false, false, false, true);

    private static LoreEntry[] BuildAll()
    {
        var list = new List<LoreEntry>();
        void I(LoreEntry e) => Add(list, e);

        // ---- GENERAL (23) ----
        I(C("g01", "JOSEPH HAS ENTERED THE FACILITY.", LoreCategory.General));
        I(C("g02", "THE IT DEPARTMENT HAS BEEN INFORMED.", LoreCategory.General));
        I(C("g03", "NADDAF PROTOCOL ACTIVE.", LoreCategory.General));
        I(C("g04", "JOSEPH DEPLOYMENT CONFIRMED.", LoreCategory.General));
        I(C("g05", "THIS WAS NOT IN THE TICKET.", LoreCategory.General));
        I(C("g06", "OPERATION NORMAL DAY CONTINUES.", LoreCategory.General));
        I(C("g07", "ALL SYSTEMS REPORTING TO JOSEPH.", LoreCategory.General));
        I(C("g08", "THE ROOM HAS BEEN TECHNICALLY SECURED.", LoreCategory.General));
        I(C("g09", "JOSEPH HAS ACKNOWLEDGED THE INCIDENT.", LoreCategory.General));
        I(C("g10", "UNAUTHORIZED COMPETENCE DETECTED.", LoreCategory.General));
        I(C("g11", "THIS MACHINE NOW BELONGS TO IT.", LoreCategory.General));
        I(C("g12", "THE BUILDING HOLDS ITS BREATH.", LoreCategory.General));
        I(C("g13", "JOSEPH COMPETENCE LEVEL: UNREASONABLE.", LoreCategory.General));
        I(C("g14", "THE TICKET QUEUE HAS ENTERED A SAFER PLACE.", LoreCategory.General));
        I(C("g15", "SOMEONE HAS BEEN SPARED TODAY.", LoreCategory.General));
        I(U("g16", "THE KEYBOARD IS LOADED.", LoreCategory.General));
        I(U("g17", "JOSEPH HAS SPOKEN. THE ROOM LISTENS.", LoreCategory.General));
        I(U("g18", "A LEGENDARY LEVEL OF NORMAL IS OCCURRING.", LoreCategory.General));
        I(U("g19", "THE SERVER ROOM SMELLS OF CONFIDENCE.", LoreCategory.General));
        I(U("g20", "ANCIENT IT RITUALS ARE BEING PERFORMED.", LoreCategory.General));
        I(R("g21", "JOSEPH HAS DONE THE IMPOSSIBLE AGAIN.", LoreCategory.General));
        I(R("g22", "THE FACILITY HAS REACHED FULL JOSEPH MEASURES.", LoreCategory.General));
// ---- IT WIZARD (20) ----
        I(C("it01", "IT WIZARD STATUS: CONFIRMED.", LoreCategory.ItWizard));
        I(C("it02", "THE TICKET HAS ASCENDED.", LoreCategory.ItWizard));
        I(C("it03", "LEVEL 99 TROUBLESHOOTING DETECTED.", LoreCategory.ItWizard));
        I(C("it04", "JOSEPH HAS READ THE ERROR MESSAGE.", LoreCategory.ItWizard));
        I(C("it05", "THE PASSWORD WAS, IN FACT, CORRECT.", LoreCategory.ItWizard));
        I(C("it06", "REBOOT AUTHORIZATION GRANTED.", LoreCategory.ItWizard));
        I(C("it07", "THE CABLE WAS LOOSE.", LoreCategory.ItWizard));
        I(C("it08", "DNS HAS BEEN PERSONALLY WARNED.", LoreCategory.ItWizard));
        I(C("it09", "THE DRIVER HAS BEEN LOCATED.", LoreCategory.ItWizard));
        I(C("it10", "JOSEPH HAS ENTERED DEVICE MANAGER.", LoreCategory.ItWizard));
        I(C("it11", "THE HELP DESK HAS BECOME SENTIENT.", LoreCategory.ItWizard));
        I(C("it12", "THE ISSUE HAS BEEN ESCALATED TO JOSEPH.", LoreCategory.ItWizard));
        I(C("it13", "TECHNICAL DEBT HAS BEEN OBSERVED.", LoreCategory.ItWizard));
        I(C("it14", "JOSEPH HAS CLEARED THE CACHE.", LoreCategory.ItWizard));
        I(C("it15", "THE SERVER FEARS HIM.", LoreCategory.ItWizard));
        I(U("it16", "JOSEPH HAS ENTERED COMMAND PROMPT. FEAR SPREADS.", LoreCategory.ItWizard));
        I(U("it17", "THE FIREWALL HAS APOLOGIZED.", LoreCategory.ItWizard));
        I(U("it18", "JOSEPH HAS COUNTERED THE LAYER 8 ERROR.", LoreCategory.ItWizard));
        I(U("it19", "THE TICKET HAS BEEN SPEEDRUN.", LoreCategory.ItWizard));
        I(R("it20", "JOSEPH HAS COMPILED THE UNCOMPILABLE.", LoreCategory.ItWizard));

        // ---- ROUTER RESTORATION (20) ----
        I(C("rt01", "THE GREAT ROUTER RESTORATION HAS BEGUN.", LoreCategory.RouterRestoration));
        I(C("rt02", "ROUTER RESTORATION: PHASE TWO.", LoreCategory.RouterRestoration));
        I(C("rt03", "NOBODY TOUCH THE ROUTER.", LoreCategory.RouterRestoration));
        I(C("rt04", "THE ROUTER HAS BEEN FORGIVEN.", LoreCategory.RouterRestoration));
        I(C("rt05", "PACKET FLOW RESTORED.", LoreCategory.RouterRestoration));
        I(C("rt06", "THE ETHERNET COUNCIL APPROVES.", LoreCategory.RouterRestoration));
        I(C("rt07", "THE ROUTER HAS RETURNED TO SERVICE.", LoreCategory.RouterRestoration));
        I(C("rt08", "LATENCY HAS BEEN SENT HOME.", LoreCategory.RouterRestoration));
        I(C("rt09", "THE ACCESS POINT HAS REPENTED.", LoreCategory.RouterRestoration));
        I(C("rt10", "WI-FI INTEGRITY RESTORED.", LoreCategory.RouterRestoration));
        I(C("rt11", "THE GREAT REBOOT WAS SUCCESSFUL.", LoreCategory.RouterRestoration));
        I(C("rt12", "JOSEPH HAS RESTORED THE NETWORK.", LoreCategory.RouterRestoration));
        I(C("rt13", "THE WAN HAS BEEN PACIFIED.", LoreCategory.RouterRestoration));
        I(C("rt14", "THE LAN IS STABLE.", LoreCategory.RouterRestoration));
        I(C("rt15", "THE ROUTER REMEMBERS.", LoreCategory.RouterRestoration));
        I(U("rt16", "ROUTER RESTORATION COMPLETE. COMMENDATION PENDING.", LoreCategory.RouterRestoration));
        I(U("rt17", "THE MODEM HAS BEEN COUNSELED.", LoreCategory.RouterRestoration));
        I(U("rt18", "PACKETS NOW FLOW WITH RESPECT.", LoreCategory.RouterRestoration));
        I(U("rt19", "THE ROUTER HAS PROMISED TO BEHAVE.", LoreCategory.RouterRestoration));
        I(R("rt20", "THE ROUTER HAS ASCENDED PAST RESTARTING.", LoreCategory.RouterRestoration));

        // ---- SHAWARMA (15) ----
        I(C("sh01", "SHAWARMA RESERVES: STABLE.", LoreCategory.Shawarma));
        I(C("sh02", "EMERGENCY SHAWARMA FUND: CLASSIFIED.", LoreCategory.Shawarma));
        I(C("sh03", "SHAWARMA PACKAGE SECURED.", LoreCategory.Shawarma));
        I(C("sh04", "GARLIC SAUCE LEVELS NOMINAL.", LoreCategory.Shawarma));
        I(C("sh05", "THE WRAP REMAINS INTACT.", LoreCategory.Shawarma));
        I(C("sh06", "SHAWARMA LOGISTICS ARE OPERATIONAL.", LoreCategory.Shawarma));
        I(C("sh07", "THE ROTISSERIE HAS BEEN NOTIFIED.", LoreCategory.Shawarma));
        I(C("sh08", "EXTRA PICKLES AUTHORIZED.", LoreCategory.Shawarma));
        I(C("sh09", "THE SHAWARMA WINDOW IS OPEN.", LoreCategory.Shawarma));
        I(C("sh10", "CALORIC SUPPORT HAS ARRIVED.", LoreCategory.Shawarma));
        I(U("sh11", "TACTICAL SHAWARMA DEPLOYED.", LoreCategory.Shawarma));
        I(U("sh12", "THE GARLIC SAUCE SITUATION IS UNDER CONTROL.", LoreCategory.Shawarma));
        I(U("sh13", "A SINGLE SHAWARMA CAN FIX MANY THINGS.", LoreCategory.Shawarma));
        I(R("sh14", "THE SHAWARMA HAS GRACED THE SERVER ROOM.", LoreCategory.Shawarma));
        I(R("sh15", "SHAWARMA STOCKPILE: UNCONFIRMED BUT CONFIDENT.", LoreCategory.Shawarma));
        I(Lg("g23", "JOSEPH IS ETERNALLY UNIMPRESSED BY SERVERS.", LoreCategory.General));

        // ---- HONDA CIVIC (15) ----
        I(C("cv01", "CIVIC STATUS: DEPLOYED.", LoreCategory.HondaCivic));
        I(C("cv02", "THE CIVIC HAS LEFT THE GARAGE.", LoreCategory.HondaCivic));
        I(C("cv03", "VTEC CONDITIONS APPROACHING.", LoreCategory.HondaCivic));
        I(C("cv04", "CIVIC SUPPORT IS EN ROUTE.", LoreCategory.HondaCivic));
        I(C("cv05", "THE 4-CYLINDER RESPONSE TEAM HAS ARRIVED.", LoreCategory.HondaCivic));
        I(C("cv06", "CIVIC TELEMETRY: NOMINAL.", LoreCategory.HondaCivic));
        I(C("cv07", "JOSEPH HAS PARKED WITH PURPOSE.", LoreCategory.HondaCivic));
        I(C("cv08", "THE CIVIC REMAINS MISSION READY.", LoreCategory.HondaCivic));
        I(C("cv09", "THE HONDA DIVISION HAS BEEN ACTIVATED.", LoreCategory.HondaCivic));
        I(C("cv10", "COMPACT SEDAN, MAXIMUM AUTHORITY.", LoreCategory.HondaCivic));
        I(U("cv11", "THE CIVIC HAS ENTERED THE AO.", LoreCategory.HondaCivic));
        I(U("cv12", "FUEL ECONOMY IS TEMPORARILY CLASSIFIED.", LoreCategory.HondaCivic));
        I(U("cv14", "THE CIVIC DOES NOT ACKNOWLEDGE DEFEAT.", LoreCategory.HondaCivic));
        I(U("cv15", "CIVIC TRANSPORT: IT'S PERSONAL.", LoreCategory.HondaCivic));
        I(R("cv13", "THE CIVIC HAS REQUESTED ADMIN RIGHTS.", LoreCategory.HondaCivic));

        // ---- MASSAGE CHAIR (12) ----
        I(C("mc01", "MASSAGE CHAIR RECOVERY MODE.", LoreCategory.MassageChair));
        I(C("mc02", "THERAPEUTIC OPERATIONS COMMENCING.", LoreCategory.MassageChair));
        I(C("mc03", "LUMBAR SUPPORT: MAXIMUM.", LoreCategory.MassageChair));
        I(C("mc04", "COMMAND HAS RECLINED.", LoreCategory.MassageChair));
        I(C("mc05", "THE CHAIR HAS ASSUMED CONTROL.", LoreCategory.MassageChair));
        I(C("mc06", "RECOVERY CYCLE ACTIVE.", LoreCategory.MassageChair));
        I(C("mc07", "JOSEPH HAS ENTERED ZERO-G.", LoreCategory.MassageChair));
        I(C("mc08", "MASSAGE INTENSITY: TACTICAL.", LoreCategory.MassageChair));
        I(C("mc09", "THE COMMAND CENTER IS RECLINING.", LoreCategory.MassageChair));
        I(U("mc10", "POST-INCIDENT RECOVERY AUTHORIZED.", LoreCategory.MassageChair));
        I(U("mc11", "VIBRATION SYSTEMS NOMINAL.", LoreCategory.MassageChair));
        I(R("mc12", "THE MASSAGE CHAIR HAS BECOME THE PRIMARY SERVER.", LoreCategory.MassageChair));

        // ---- PRINTER BOSS FIGHT (15) ----
        I(C("pb01", "PRINTER BOSS FIGHT INITIATED.", LoreCategory.PrinterBossFight));
        I(C("pb02", "PAPER JAM DETECTED.", LoreCategory.PrinterBossFight));
        I(C("pb03", "TONER HOSTILITY CONFIRMED.", LoreCategory.PrinterBossFight));
        I(C("pb04", "THE PRINTER HAS CHOSEN VIOLENCE.", LoreCategory.PrinterBossFight));
        I(C("pb05", "TRAY 2 HAS BETRAYED US.", LoreCategory.PrinterBossFight));
        I(C("pb06", "PC LOAD LETTER REMAINS UNEXPLAINED.", LoreCategory.PrinterBossFight));
        I(C("pb07", "THE PRINTER IS OFFLINE FOR PERSONAL REASONS.", LoreCategory.PrinterBossFight));
        I(C("pb08", "DRIVER INSTALLATION PHASE TWO.", LoreCategory.PrinterBossFight));
        I(C("pb09", "JOSEPH HAS ENTERED SINGLE COMBAT.", LoreCategory.PrinterBossFight));
        I(C("pb10", "THE PRINT QUEUE HAS BEEN PURGED.", LoreCategory.PrinterBossFight));
        I(U("pb11", "THE PRINTER HAS BEEN DEFEATED.", LoreCategory.PrinterBossFight));
        I(U("pb12", "TONER LEVELS ARE A MATTER OF NATIONAL SECURITY.", LoreCategory.PrinterBossFight));
        I(U("pb14", "THE PRINTER HAS BEEN COUNSELED HEAVILY.", LoreCategory.PrinterBossFight));
        I(U("pb15", "A NEW TONER HAS BEEN DETAINED.", LoreCategory.PrinterBossFight));
        I(R("pb13", "THE PRINTER WORKED ON THE FIRST TRY.", LoreCategory.PrinterBossFight));

        // ---- MAXIMUM NADDAF (11) ----
        I(C("mn01", "MAXIMUM NADDAF CONDITIONS DETECTED.", LoreCategory.MaximumNaddaf));
        I(C("mn02", "NADDAF LEVELS EXCEED SAFE LIMITS.", LoreCategory.MaximumNaddaf));
        I(C("mn03", "THIS IS AN UNAUTHORIZED AMOUNT OF JOSEPH.", LoreCategory.MaximumNaddaf));
        I(C("mn04", "THE SYSTEM HAS ENTERED MAXIMUM NADDAF.", LoreCategory.MaximumNaddaf));
        I(C("mn05", "JOSEPH DENSITY CRITICAL.", LoreCategory.MaximumNaddaf));
        I(U("mn06", "NADDAF PROTOCOL: FULL DEPLOYMENT.", LoreCategory.MaximumNaddaf));
        I(U("mn07", "THE FACILITY CANNOT CONTAIN THIS.", LoreCategory.MaximumNaddaf));
        I(U("mn08", "MAXIMUM NADDAF AUTHORIZED.", LoreCategory.MaximumNaddaf));
        I(R("mn09", "THE ROUTER WAS NOT DESIGNED FOR THIS.", LoreCategory.MaximumNaddaf));
        I(R("mn10", "ALL DEPARTMENTS REPORT JOSEPH.", LoreCategory.MaximumNaddaf));
        I(U("mn11", "NADDAF ACTIVITY DETECTED.", LoreCategory.MaximumNaddaf));

        // ---- CS2 (25) ----
        I(Ctx("cs01", "TACTICAL JOSEPH CONFIRMED.", LoreCategory.Cs2, LoreTrigger.Cs2Kill, "cs2", "kill"));
        I(Ctx("cs02", "TARGET RESOLVED.", LoreCategory.Cs2, LoreTrigger.Cs2Kill, "cs2", "kill"));
        I(Ctx("cs03", "JOSEPH HAS CLOSED THE TICKET.", LoreCategory.Cs2, LoreTrigger.Cs2Kill, "cs2", "kill"));
        I(Ctx("cs04", "INCIDENT REPORT FILED.", LoreCategory.Cs2, LoreTrigger.Cs2Death, "cs2", "death"));
        I(Ctx("cs05", "JOSEPH REQUESTS A RESTART.", LoreCategory.Cs2, LoreTrigger.Cs2Death, "cs2", "death"));
        I(Ctx("cs06", "RECOVERY MODE AUTHORIZED.", LoreCategory.Cs2, LoreTrigger.Cs2Death, "cs2", "death"));
        I(Ctx("cs07", "ROUND SECURED. ROUTER STABLE.", LoreCategory.Cs2, LoreTrigger.Cs2RoundWin, "cs2", "round_win"));
        I(Ctx("cs08", "IT WIZARD VICTORY CONFIRMED.", LoreCategory.Cs2, LoreTrigger.Cs2RoundWin, "cs2", "round_win"));
        I(Ctx("cs09", "ROUTER SECURED.", LoreCategory.Cs2, LoreTrigger.Cs2BombDefuse, "cs2", "bomb_defuse"));
        I(Ctx("cs10", "DEVICE DISCONNECTED SAFELY.", LoreCategory.Cs2, LoreTrigger.Cs2BombDefuse, "cs2", "bomb_defuse"));
        I(Ctx("cs11", "THE RED WIRE HAS BEEN ESCALATED.", LoreCategory.Cs2, LoreTrigger.Cs2BombDefuse, "cs2", "bomb_defuse"));
        I(Ctx("cs12", "UNAUTHORIZED HARDWARE INSTALLED.", LoreCategory.Cs2, LoreTrigger.Cs2BombPlant, "cs2", "bomb_plant"));
        I(Ctx("cs13", "DEVICE DEPLOYMENT DETECTED.", LoreCategory.Cs2, LoreTrigger.Cs2BombPlant, "cs2", "bomb_plant"));
        I(Ctx("cs14", "EMPLOYEE OF THE ROUND.", LoreCategory.Cs2, LoreTrigger.Cs2Mvp, "cs2", "mvp"));
        I(Ctx("cs15", "JOSEPH HAS BEEN CC'D.", LoreCategory.Cs2, LoreTrigger.Cs2Mvp, "cs2", "mvp"));
        I(Ctx("cs16", "THE HELP DESK IS OVERCAPACITY.", LoreCategory.Cs2, LoreTrigger.Cs2Multikill, "cs2", "multi_kill"));
        I(Ctx("cs17", "MULTIKILL: THE INCIDENT REPORT IS SIX PAGES LONG.", LoreCategory.Cs2, LoreTrigger.Cs2Multikill, "cs2", "multi_kill"));
        I(C("cs18", "CS2 INTEGRATION ONLINE.", LoreCategory.Cs2, LoreTrigger.Cs2Kill, "cs2", "attach"));
        I(C("cs19", "THE ROUTER IS APPLAUDING TACTICALLY.", LoreCategory.Cs2, LoreTrigger.Cs2RoundWin, "cs2", "round_win"));
        I(U("cs20", "JOSEPH NADDAF DISABLED THE BOMB BY STARING.", LoreCategory.Cs2, LoreTrigger.Cs2BombDefuse, "cs2", "bomb_defuse"));
        I(U("cs21", "A MULTIKILL HAS OPENED A PRIORITY TICKET.", LoreCategory.Cs2, LoreTrigger.Cs2Multikill, "cs2", "multi_kill"));
        I(U("cs22", "THE DEATH WAS FILED AS A CHANGE REQUEST.", LoreCategory.Cs2, LoreTrigger.Cs2Death, "cs2", "death"));
        I(R("cs23", "JOSEPH ACE'D THE ROUND. THE HELP DESK APPLAUDS.", LoreCategory.Cs2, LoreTrigger.Cs2RoundWin, "cs2", "round_win"));
        I(R("cs24", "TACTICAL SHAWARMA RESTORED THE ROUTER MID-ROUND.", LoreCategory.Cs2, LoreTrigger.Cs2RoundWin, "cs2", "round_win"));
        I(Lg("cs25", "JOSEPH WON THE ROUND WITHOUT EVERYONE NOTICING.", LoreCategory.Cs2, LoreTrigger.Cs2RoundWin, "cs2", "round_win"));

        // ---- HALF-LIFE 2 (15) ----
        I(Ctx("hl01", "JOSEPH HAS ENTERED CITY 17.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl02", "COMBINE NETWORK COMPROMISED.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl03", "CROWBAR STATUS: DEPLOYED.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl04", "THE RESISTANCE HAS RESTORED THE ROUTER.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl05", "NOBODY TOUCH THE CITADEL ROUTER.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl06", "CIVIC STATUS: RESISTANCE VEHICLE.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl07", "DR. NADDAF REPORTING FOR DUTY.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl08", "THE HEV SUIT REQUIRES IT SUPPORT.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl09", "HEADCRAB INCIDENT ESCALATED.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl10", "CITADEL WI-FI IS UNSTABLE.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl11", "THE RESISTANCE HAS OPENED A TICKET.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(Ctx("hl12", "GRAVITY GUN DRIVER INSTALLED.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(U("hl13", "JOSEPH TROUBLESHOOTED THE GRAVITY GUN.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(U("hl14", "THE CITADEL ROUTER NOW SERVES THE RESISTANCE.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));
        I(R("hl15", "JOSEPH DISABLED THE COMBINE NETWORK WITH A REBOOT.", LoreCategory.HalfLife2, LoreTrigger.HalfLife2Event, "hl2", "session"));

        // ---- MW2 2009 (15) ----
        I(Ctx("mw01", "TACTICAL JOSEPH DEPLOYED.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw02", "UAV ONLINE. SHAWARMA SECURED.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw03", "CIVIC INBOUND.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw04", "ROUTER RESTORATION TEAM EN ROUTE.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw05", "JOSEPH NADDAF IS OSCAR MIKE.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw06", "HOSTILE WI-FI DETECTED.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw07", "COMMAND HAS APPROVED MAXIMUM NADDAF.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw08", "TACTICAL SHAWARMA PACKAGE DELIVERED.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw09", "IT SUPPORT IS DANGER CLOSE.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(Ctx("mw10", "THE HELP DESK HAS CALLED IN A UAV.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(U("mw11", "JOSEPH FLANKED THE PING.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(U("mw12", "MAXIMUM NADDAF APPROVED FOR THIS SKIRMISH.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(U("mw13", "ROUTER RESTORATION: TACTICAL INSERTION.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(R("mw14", "HE GOT THE FINAL KILL WHILE OPENING A TICKET.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));
        I(R("mw15", "THE HELP DESK CALLED IN AN AIRSTRIKE.", LoreCategory.Mw22009, LoreTrigger.Mw22009Event, "mw2", "session"));

        // ---- CLOUD / SYNC (15) ----
        I(C("cl01", "ARCHIVE SYNCHRONIZED.", LoreCategory.CloudSync, LoreTrigger.CloudSyncSuccess));
        I(C("cl02", "SHAWARMA RESERVES SECURE.", LoreCategory.CloudSync, LoreTrigger.CloudSyncSuccess));
        I(C("cl03", "JOSEPH CLOUD LINK RESTORED.", LoreCategory.CloudSync, LoreTrigger.CloudSyncSuccess));
        I(C("cl04", "REMOTE ARCHIVE NOMINAL.", LoreCategory.CloudSync, LoreTrigger.CloudSyncSuccess));
        I(C("cl05", "THE LIBRARY HAS BEEN RECONCILED.", LoreCategory.CloudSync, LoreTrigger.CloudSyncSuccess));
        I(U("cl06", "SUPREME CLOUD AUTHORITY CONFIRMED.", LoreCategory.CloudSync, LoreTrigger.CloudSyncSuccess));
        I(C("cl07", "CLOUD LINK EXPERIENCING CHARACTER DEVELOPMENT.", LoreCategory.CloudSync, LoreTrigger.CloudSyncFailure));
        I(C("cl08", "REMOTE ARCHIVE HAS FILED A COMPLAINT.", LoreCategory.CloudSync, LoreTrigger.CloudSyncFailure));
        I(C("cl09", "SYNC REQUEST ESCALATED.", LoreCategory.CloudSync, LoreTrigger.CloudSyncFailure));
        I(C("cl10", "THE CLOUD REQUIRES A REBOOT.", LoreCategory.CloudSync, LoreTrigger.CloudSyncFailure));
        I(U("cl11", "REMOTE JOSEPH UNAVAILABLE.", LoreCategory.CloudSync, LoreTrigger.CloudSyncFailure));
        I(C("cl12", "GREAT ROUTER RESTORATION COMPLETE.", LoreCategory.Recovery, LoreTrigger.Recovery));
        I(C("cl13", "IT WIZARD LINK RESTORED.", LoreCategory.Recovery, LoreTrigger.Recovery));
        I(C("cl14", "SYSTEM HAS RETURNED FROM MASSAGE CHAIR MODE.", LoreCategory.Recovery, LoreTrigger.Recovery));
        I(C("cl15", "THE TICKET HAS BEEN RESOLVED.", LoreCategory.Recovery, LoreTrigger.Recovery));

        // ---- NADD / MARKET (15) ----
        I(C("mk01", "NADDAF MARKET SENTIMENT: UNREASONABLY CONFIDENT.", LoreCategory.MarketNadd, LoreTrigger.MarketPositive));
        I(C("mk02", "SHAWARMA LIQUIDITY IMPROVING.", LoreCategory.MarketNadd, LoreTrigger.MarketPositive));
        I(C("mk03", "THE CIVIC INDEX IS RISING.", LoreCategory.MarketNadd, LoreTrigger.MarketPositive));
        I(U("mk04", "THE ROUTER IS RIDING A GREEN CANDLE.", LoreCategory.MarketNadd, LoreTrigger.MarketPositive));
        I(C("mk05", "NADDAF MARKET HAS ENTERED MASSAGE CHAIR RECOVERY.", LoreCategory.MarketNadd, LoreTrigger.MarketNegative));
        I(C("mk06", "VOLATILITY HAS OPENED A TICKET.", LoreCategory.MarketNadd, LoreTrigger.MarketNegative));
        I(C("mk07", "THE ROUTER DENIES RESPONSIBILITY.", LoreCategory.MarketNadd, LoreTrigger.MarketNegative));
        I(U("mk08", "THE MARKET HAS UNPLUGGED ITSELF.", LoreCategory.MarketNadd, LoreTrigger.MarketNegative));
        I(C("mk09", "SHAWARMA RESERVES HOLD STEADY.", LoreCategory.MarketNadd, LoreTrigger.MarketFlat));
        I(C("mk10", "NADDAF MARKET AWAITS FURTHER INSTRUCTIONS.", LoreCategory.MarketNadd, LoreTrigger.MarketFlat));
        I(U("mk11", "THE NASDAQ NADDAF IS FOLDING ARMS.", LoreCategory.MarketNadd, LoreTrigger.MarketFlat));
        I(U("mk12", "MARKET DATA FILED UNDER TICKET 8XX.", LoreCategory.MarketNadd, LoreTrigger.MarketFlat));
        I(R("mk13", "THE CIVIC INDEX HIT AN ALL-TIME HIGH.", LoreCategory.MarketNadd, LoreTrigger.MarketPositive));
        I(R("mk14", "VOLATILITY WAS SENT TO THE ROUTER FOR COUNSELING.", LoreCategory.MarketNadd, LoreTrigger.MarketNegative));
        I(Lg("mk15", "THE MARKET HAS BEEN PROFESSIONALLY STARRED AT.", LoreCategory.MarketNadd, LoreTrigger.MarketFlat));

        // ---- AUDIO (8) ----
        I(C("au01", "AUDIO SYSTEMS ARMED.", LoreCategory.Audio, LoreTrigger.AudioPlayed));
        I(C("au02", "JOSEPH HAS ENTERED THE PA SYSTEM.", LoreCategory.Audio, LoreTrigger.AudioPlayed));
        I(C("au03", "SOUND CHECK: NADDAF.", LoreCategory.Audio, LoreTrigger.AudioPlayed));
        I(C("au04", "THE SPEAKERS HAVE BEEN WARNED.", LoreCategory.Audio, LoreTrigger.AudioPlayed));
        I(C("au05", "AUDIO DEPLOYMENT COMPLETE.", LoreCategory.Audio, LoreTrigger.AudioPlayed));
        I(U("au06", "VOLUME HAS BEEN ESCALATED TO THE FULL TICKET BOARD.", LoreCategory.Audio, LoreTrigger.AudioPlayed));
        I(U("au07", "THE PA SYSTEM NOW ANSWERS TO JOSEPH.", LoreCategory.Audio, LoreTrigger.AudioPlayed));
        I(R("au08", "AUDIO ENGINEER NADDAF HAS LEFT THE BUILDING.", LoreCategory.Audio, LoreTrigger.AudioPlayed));

        // ---- ERROR (5 - real info stays first ----
        I(C("er01", "THE PRINTER BOSS FIGHT HAS INITIATED WITHOUT PERMISSION.", LoreCategory.Error, LoreTrigger.Error));
        I(C("er02", "THE ISSUE HAS BEEN ESCALATED UP THE CHAIN.", LoreCategory.Error, LoreTrigger.Error));
        I(C("er03", "ROUTER RESTORATION MAY BE REQUIRED.", LoreCategory.Error, LoreTrigger.Error));
        I(U("er04", "THE MACHINE HAS BECOME EMOTIONAL.", LoreCategory.Error, LoreTrigger.Error));
        I(R("er05", "THE ERROR HAS BEEN PROFESSIONALLY DIAGNOSED.", LoreCategory.Error, LoreTrigger.Error));

        // ---- UPDATER (8) ----
        I(C("up01", "NEW JOSEPH DEPLOYMENT AVAILABLE.", LoreCategory.Updater, LoreTrigger.UpdateAvailable));
        I(C("up02", "IT HAS REQUESTED A PATCH WINDOW.", LoreCategory.Updater, LoreTrigger.UpdateAvailable));
        I(C("up03", "ROUTER RESTORATION PACKAGE READY.", LoreCategory.Updater, LoreTrigger.UpdateAvailable));
        I(C("up04", "THE UPDATE HAS BEEN APPROVED BY MANAGEMENT.", LoreCategory.Updater, LoreTrigger.UpdateAvailable));
        I(U("up05", "A NEWER JOSEPH HAS DEPLOYED SUCCESSFULLY.", LoreCategory.Updater, LoreTrigger.UpdateInstalled));
        I(U("up06", "THE PATCH HAS BEEN INSTALLED. FEAR REFRESHED.", LoreCategory.Updater, LoreTrigger.UpdateInstalled));
        I(R("up07", "THE UPDATER DIDN'T NEED TO REBOOT.", LoreCategory.Updater, LoreTrigger.UpdateInstalled));
        I(Lg("up08", "THE UPDATE INSTALLED ITSELF OUT OF RESPECT.", LoreCategory.Updater, LoreTrigger.UpdateInstalled));

        // ---- MILESTONE (12, one-time flavor) ----
        I(M("ms10", "NADDAF ACTIVITY DETECTED IN THE FACILITY.", 10, 10));
        I(M("ms25", "THE HELP DESK IS GETTING CONCERNED.", 25, 25));
        I(M("ms50", "JOSEPH DEPLOYMENT FREQUENCY ELEVATED.", 50, 50));
        I(M("ms100", "MAXIMUM NADDAF AUTHORIZATION PENDING.", 100, 100));
        I(M("ms250", "THE ROUTER HAS REQUESTED PTO.", 250, 250));
        I(M("ms500", "THIS MACHINE HAS SEEN TOO MUCH.", 500, 500));
        I(M("ms1000", "THE GREAT ROUTER RESTORATION IS NOW HISTORICAL RECORD.", 1000, 1000));

        // ---- RARE / LEGENDARY EASTER EGGS (15) ----
        I(R("e01", "JOSEPH HAS READ THE DOCUMENTATION.", LoreCategory.Rare));
        I(R("e02", "THE PRINTER WORKED ON THE FIRST ATTEMPT.", LoreCategory.Rare));
        I(R("e03", "DNS WAS NOT THE PROBLEM.", LoreCategory.Rare));
        I(R("e04", "THE USER RESTARTED BEFORE CALLING IT.", LoreCategory.Rare));
        I(R("e05", "THE ROUTER HAS ACHIEVED INNER PEACE.", LoreCategory.Rare));
        I(R("e06", "SHAWARMA.EXE HAS STOPPED RESPONDING.", LoreCategory.Rare));
        I(R("e07", "JOSEPH HAS CLOSED THE TICKET WITHOUT REBOOTING.", LoreCategory.Rare));
        I(R("e08", "THE INCIDENT WAS ACTUALLY USER ERROR.", LoreCategory.Rare));
        I(Lg("e09", "THE CIVIC HAS REQUESTED ADMIN PRIVILEGES.", LoreCategory.Legendary));
        I(Lg("e10", "THE MASSAGE CHAIR IS NOW THE PRIMARY SERVER.", LoreCategory.Legendary));
        I(Lg("e11", "NADDAF HAS BEEN PROMOTED TO SYSTEMS ADMIN.", LoreCategory.Legendary));
        I(Lg("e12", "THE ROUTER IS NOW DISPENSING SHAWARMA.", LoreCategory.Legendary));
        I(Lg("e13", "JOSEPH HAS COMPLETED THE ENTIRE BACKLOG IN ONE SESSION.", LoreCategory.Legendary));
        I(Lg("e14", "THE SERVER ROOM IS NOW A CIVIC.", LoreCategory.Legendary));
        I(Lg("e15", "LEGENDARY NADDAF MODE ACHIEVED.", LoreCategory.Legendary));

        return list.ToArray();
    }
}