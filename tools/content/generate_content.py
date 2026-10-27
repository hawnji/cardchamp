#!/usr/bin/env python3
"""Generates Assets/CardVault/Resources/Content/cardvault_content.json.

Content is authored here as compact Python data and expanded into the JSON the game loads.
All names, teams, leagues, brands and universes are fictional. Re-run after editing:

    python3 tools/content/generate_content.py
"""
import json
import os

DAY = 86400
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "CardVault", "Resources", "Content", "cardvault_content.json")

VALUE = {"Common": 5, "Uncommon": 15, "Rare": 60, "SuperRare": 200, "Legendary": 800}


def reward(credits=0, tickets=0, cosmetics=(), cards=(), unlocks=(), slots=0, xp=0):
    r = {"credits": credits, "eventTickets": tickets, "cosmeticIds": list(cosmetics), "cards": list(cards),
         "unlockListingIds": list(unlocks), "showcaseSlots": slots, "collectorXp": xp}
    return r


def card_grant(template, parallel=None, tradable=False, note=None):
    g = {"templateId": template, "tradable": tradable, "condition": 10.0}
    if parallel:
        g["parallelId"] = parallel
    if note:
        g["note"] = note
    return g


brands = [
    {"id": "fieldhouse", "name": "Fieldhouse", "category": "Sports", "description": "The flagship everyday sports-card brand."},
    {"id": "apex-diamond", "name": "Apex Diamond", "category": "Sports", "description": "Premium on-card autographs and low print runs."},
    {"id": "heritage-lane", "name": "Heritage Lane", "category": "Sports", "description": "Classic cardboard from the golden era of collecting."},
    {"id": "mythforge", "name": "Mythforge", "category": "Tcg", "description": "Epic fantasy trading cards set in the world of Aetherfall."},
    {"id": "starbound", "name": "Starbound Saga", "category": "Tcg", "description": "Space-opera trading cards from the Nova Reach frontier."},
]

universes = [
    {"id": "aetherfall", "name": "Aetherfall", "description": "Floating isles, storm dragons and rune-bound wardens."},
    {"id": "nova-reach", "name": "Nova Reach", "description": "Frontier colonies and rogue starship captains at the edge of mapped space."},
]

leagues = [
    {"id": "chl", "name": "Continental Hoops League", "sport": "Basketball"},
    {"id": "pbu", "name": "Pacific Baseball Union", "sport": "Baseball"},
]

teams = [
    {"id": "harbor-hawks", "name": "Hawks", "city": "Harbor City", "leagueId": "chl", "primaryColor": "#1F4E8C", "secondaryColor": "#F2B705"},
    {"id": "summit-peaks", "name": "Peaks", "city": "Summit", "leagueId": "chl", "primaryColor": "#2E6B3F", "secondaryColor": "#E8E8E8"},
    {"id": "ironvale-forge", "name": "Forge", "city": "Ironvale", "leagueId": "chl", "primaryColor": "#8C2F1F", "secondaryColor": "#2B2B2B"},
    {"id": "lakeshore-tides", "name": "Tides", "city": "Lakeshore", "leagueId": "chl", "primaryColor": "#127C8C", "secondaryColor": "#F0F0F0"},
    {"id": "redrock-rattlers", "name": "Rattlers", "city": "Redrock", "leagueId": "pbu", "primaryColor": "#B3412A", "secondaryColor": "#F2D7B6"},
    {"id": "bayport-mariners", "name": "Navigators", "city": "Bayport", "leagueId": "pbu", "primaryColor": "#16325C", "secondaryColor": "#C9A227"},
    {"id": "pinecrest-owls", "name": "Owls", "city": "Pinecrest", "leagueId": "pbu", "primaryColor": "#4A3B69", "secondaryColor": "#D9D9D9"},
    {"id": "goldfield-miners", "name": "Prospectors", "city": "Goldfield", "leagueId": "pbu", "primaryColor": "#3B3B3B", "secondaryColor": "#E3B341"},
]

athletes_hoops = [
    ("mara-voss", "Mara Voss", "harbor-hawks", "Guard"), ("dex-okafor", "Dex Okafor", "harbor-hawks", "Center"),
    ("jalen-reyes", "Jalen Reyes", "summit-peaks", "Forward"), ("tobias-lind", "Tobias Lind", "summit-peaks", "Guard"),
    ("kai-montgomery", "Kai Montgomery", "ironvale-forge", "Forward"), ("andre-castell", "Andre Castell", "ironvale-forge", "Center"),
    ("noor-haddad", "Noor Haddad", "lakeshore-tides", "Guard"), ("eli-brannigan", "Eli Brannigan", "lakeshore-tides", "Forward"),
    ("sienna-park", "Sienna Park", "harbor-hawks", "Forward"), ("marcus-oyelaran", "Marcus Oyelaran", "summit-peaks", "Center"),
]
athletes_baseball = [
    ("rafael-quinto", "Rafael Quinto", "redrock-rattlers", "Pitcher"), ("hank-delaney", "Hank Delaney", "redrock-rattlers", "Catcher"),
    ("yuki-tanabe", "Yuki Tanabe", "bayport-mariners", "Shortstop"), ("cole-ashby", "Cole Ashby", "bayport-mariners", "Outfield"),
    ("ivan-petrov", "Ivan Petrov", "pinecrest-owls", "First Base"), ("luis-cardenal", "Luis Cardenal", "pinecrest-owls", "Pitcher"),
    ("wes-holloway", "Wes Holloway", "goldfield-miners", "Third Base"), ("benny-marsh", "Benny Marsh", "goldfield-miners", "Outfield"),
]
characters_aether = [
    ("storm-drake-vael", "Vael, Storm Drake", "Storm"), ("warden-isolde", "Warden Isolde", "Rune"),
    ("ember-fox-kit", "Kit the Ember Fox", "Fire"), ("tidecaller-oren", "Oren the Tidecaller", "Water"),
    ("grove-sentinel", "Grove Sentinel", "Nature"), ("shade-lyra", "Lyra of the Shade", "Shadow"),
    ("skyforge-golem", "Skyforge Golem", "Stone"), ("aether-phoenix", "Aether Phoenix", "Fire"),
    ("rune-scribe-tam", "Tam the Rune Scribe", "Rune"), ("frost-wyrm", "Hollow Frost Wyrm", "Ice"),
]
characters_nova = [
    ("captain-rhea-cole", "Captain Rhea Cole", "Command"), ("drift-pilot-zan", "Zan, Drift Pilot", "Speed"),
    ("void-oracle", "The Void Oracle", "Psionic"), ("mech-ward-7", "Mech-Ward 7", "Tech"),
    ("nebula-runner", "Nebula Runner", "Speed"), ("ion-sentry", "Ion Sentry", "Tech"),
    ("quasar-knight", "Quasar Knight", "Command"), ("salvager-moss", "Moss the Salvager", "Tech"),
]

subjects = []
for sid, name, team, pos in athletes_hoops + athletes_baseball:
    subjects.append({"id": sid, "name": name, "kind": "Athlete", "teamId": team, "position": pos, "stats": {}})
for sid, name, el in characters_aether:
    subjects.append({"id": sid, "name": name, "kind": "Character", "universeId": "aetherfall", "element": el, "stats": {}})
for sid, name, el in characters_nova:
    subjects.append({"id": sid, "name": name, "kind": "Character", "universeId": "nova-reach", "element": el, "stats": {}})

# Fictional stat lines (deterministic) so athlete cards have backs worth reading.
for i, s in enumerate(subjects):
    if s["kind"] == "Athlete":
        if s["teamId"] in ("redrock-rattlers", "bayport-mariners", "pinecrest-owls", "goldfield-miners"):
            s["stats"] = {"AVG": ".%03d" % (240 + (i * 17) % 90), "HR": str(8 + (i * 7) % 30), "RBI": str(40 + (i * 11) % 70)}
        else:
            s["stats"] = {"PPG": "%.1f" % (9 + (i * 3.7) % 20), "RPG": "%.1f" % (2 + (i * 1.3) % 9), "APG": "%.1f" % (1 + (i * 2.1) % 8)}
    else:
        s["stats"] = {"Power": str(2 + (i * 3) % 9), "Guard": str(1 + (i * 5) % 9), "Cost": str(1 + (i * 2) % 7)}

parallels = [
    {"id": "base", "name": "Base", "printRun": 0, "valueMultiplier": 1.0, "colorHex": "#D9D9D9", "isBase": True},
    {"id": "silver", "name": "Silver Prizm", "printRun": 0, "valueMultiplier": 2.0, "colorHex": "#C0C7D1"},
    {"id": "gold50", "name": "Gold", "printRun": 50, "valueMultiplier": 8.0, "colorHex": "#E3B341"},
    {"id": "black1", "name": "Black 1/1", "printRun": 1, "valueMultiplier": 60.0, "colorHex": "#111111"},
    {"id": "gold25", "name": "Gold Refractor", "printRun": 25, "valueMultiplier": 10.0, "colorHex": "#E8B923"},
    {"id": "red10", "name": "Red Refractor", "printRun": 10, "valueMultiplier": 18.0, "colorHex": "#C8102E"},
    {"id": "superfractor1", "name": "Superfractor", "printRun": 1, "valueMultiplier": 80.0, "colorHex": "#F5D76E"},
    {"id": "holo", "name": "Holofoil", "printRun": 0, "valueMultiplier": 2.5, "colorHex": "#A6E3F0"},
    {"id": "prismatic99", "name": "Prismatic", "printRun": 99, "valueMultiplier": 6.0, "colorHex": "#B57EDC"},
    {"id": "mythic1", "name": "Mythic 1/1", "printRun": 1, "valueMultiplier": 70.0, "colorHex": "#5B2A86"},
    {"id": "starlight25", "name": "Starlight", "printRun": 25, "valueMultiplier": 9.0, "colorHex": "#2F80ED"},
    {"id": "foil94", "name": "Gold Foil Stamp", "printRun": 0, "valueMultiplier": 1.8, "colorHex": "#C9A227"},
    {"id": "expo25", "name": "Expo Exclusive", "printRun": 25, "valueMultiplier": 12.0, "colorHex": "#FF7A1A"},
    {"id": "first-edition", "name": "First Edition", "printRun": 0, "valueMultiplier": 3.0, "colorHex": "#8B5A2B"},
]

cards = []
sets = []


def rarity_for(index, total):
    frac = index / total
    if frac < 0.45:
        return "Common"
    if frac < 0.70:
        return "Uncommon"
    if frac < 0.88:
        return "Rare"
    if frac < 0.97:
        return "SuperRare"
    return "Legendary"


def make_set(set_id, name, brand, category, year, subjects_list, count, vintage=False, rookies=(), autos=(), altart=(),
             parallels_=(), universe=None, league=None, description="", milestones=None, art=None):
    ids = []
    for n in range(count):
        subj = subjects_list[n % len(subjects_list)]
        sid, sname = subj[0], subj[1]
        rar = rarity_for(n, count)
        cid = "%s-%03d" % (set_id, n + 1)
        title = sname
        if n in altart:
            title = sname + " (Alternate Art)"
        if n in autos:
            title = sname + " Autograph"
        card = {
            "id": cid, "setId": set_id, "number": n + 1, "name": title, "subjectId": sid, "rarity": rar,
            "isRookie": n in rookies, "isAutograph": n in autos, "isAlternateArt": n in altart,
            "baseValue": VALUE[rar] * (3 if n in autos else 1), "artKey": "%s/%03d" % (set_id, n + 1),
            "flavorText": "", "tags": [],
        }
        if n in rookies:
            card["tags"].append("rookie")
        cards.append(card)
        ids.append(cid)
    s = {
        "id": set_id, "name": name, "brandId": brand, "category": category, "year": year, "isVintage": vintage,
        "artKey": art or set_id, "description": description, "cardTemplateIds": ids, "parallelIds": list(parallels_),
        "milestones": milestones or [],
    }
    if universe:
        s["universeId"] = universe
    if league:
        s["leagueId"] = league
    sets.append(s)
    return s


def std_milestones(set_id, star_subject=None, chase_parallel=None, graded=False, vintage=False, rookies=True):
    m = [
        {"id": "pct25", "name": "25% Collected", "kind": "BasePercent", "threshold": 25, "autoGrant": True,
         "reward": reward(credits=150, xp=40)},
        {"id": "pct50", "name": "Halfway There", "kind": "BasePercent", "threshold": 50,
         "reward": reward(credits=300, tickets=1, xp=60)},
        {"id": "pct75", "name": "75% Collected", "kind": "BasePercent", "threshold": 75,
         "reward": reward(credits=450, cosmetics=["binder-leather"], xp=80)},
        {"id": "complete", "name": "Base Set Complete", "kind": "AllBase",
         "reward": reward(credits=1000, tickets=2, cosmetics=["frame-set-master", "title-set-master"], xp=200)},
        {"id": "rookies", "name": "Every Rookie", "kind": "AllRookies",
         "reward": reward(credits=250, cosmetics=["title-rookie-hunter"], xp=60)},
    ]
    if not rookies:
        m = [x for x in m if x["id"] != "rookies"]
    if star_subject:
        m.append({"id": "star-" + star_subject, "name": "Star Collection", "kind": "AllOfSubject", "subjectId": star_subject,
                  "reward": reward(cosmetics=["ped-marble"], xp=60)})
    if chase_parallel:
        m.append({"id": "chase-" + chase_parallel, "name": "Chase Collection", "kind": "ParallelComplete",
                  "parallelId": chase_parallel, "reward": reward(credits=2500, cosmetics=["ped-crystal"], xp=300)})
    if graded:
        m.append({"id": "graded9", "name": "Gem Set (Grade 9+)", "kind": "GradedComplete", "minGrade": 9,
                  "reward": reward(cosmetics=["shelf-glass"], tickets=2, xp=200)})
    if vintage:
        m.append({"id": "vintage-complete", "name": "Vintage Set Restored", "kind": "AllBase",
                  "reward": reward(credits=800, cosmetics=["bg-vintage-hobby-shop", "title-vintage-curator"], xp=250)})
        m = [x for x in m if x["id"] != "complete"]
    return m


hoops = [(a[0], a[1]) for a in athletes_hoops]
base_ = [(a[0], a[1]) for a in athletes_baseball]
aeth = [(c[0], c[1]) for c in characters_aether]
nova = [(c[0], c[1]) for c in characters_nova]

make_set("fh26", "Fieldhouse 2026 Hoops", "fieldhouse", "Sports", 2026, hoops, 20, rookies=(2, 5, 8, 11, 17),
         parallels_=("silver", "gold50", "black1", "expo25"), league="chl",
         description="The everyday flagship. Rookie class headlined by Sienna Park.",
         milestones=std_milestones("fh26", star_subject="mara-voss", chase_parallel="gold50", graded=True))
make_set("apex26", "Apex Diamond 2026 Baseball", "apex-diamond", "Sports", 2026, base_, 12, rookies=(1, 6),
         autos=(9, 10, 11), parallels_=("gold25", "red10", "superfractor1"), league="pbu",
         description="Premium hits: on-card autographs and refractors numbered to 25, 10 and 1.",
         milestones=std_milestones("apex26", star_subject="rafael-quinto", chase_parallel="red10"))
make_set("hl94", "Heritage Lane 1994 Baseball", "heritage-lane", "Sports", 1994, base_, 12, vintage=True, rookies=(3,),
         parallels_=("foil94",), league="pbu",
         description="Discontinued wax from 1994. Centering was never this brand's strong suit.",
         milestones=std_milestones("hl94", vintage=True, graded=True))
make_set("fhr26", "Fieldhouse Rising Rookies 2026", "fieldhouse", "Sports", 2026, hoops, 10, rookies=tuple(range(10)),
         parallels_=("silver", "gold50"), league="chl",
         description="A rookie-only short print release.",
         milestones=std_milestones("fhr26"))
make_set("aeth", "Mythforge: Aetherfall", "mythforge", "Tcg", 2026, aeth, 20, altart=(15, 17, 19),
         parallels_=("holo", "prismatic99", "mythic1"), universe="aetherfall",
         description="The storm dragons return. Alternate-art chase cards in every box.",
         milestones=std_milestones("aeth", star_subject="storm-drake-vael", chase_parallel="prismatic99", rookies=False))
make_set("nova", "Starbound Saga: Nova Reach", "starbound", "Tcg", 2026, nova, 16, altart=(14, 15),
         parallels_=("holo", "starlight25"), universe="nova-reach",
         description="Chart the frontier with Captain Rhea Cole.",
         milestones=std_milestones("nova", star_subject="captain-rhea-cole", chase_parallel="starlight25", rookies=False))
make_set("mfo01", "Mythforge Origins (2001)", "mythforge", "Tcg", 2001, aeth, 12, vintage=True,
         parallels_=("first-edition",), universe="aetherfall",
         description="The historical first print run of Mythforge.",
         milestones=std_milestones("mfo01", vintage=True, rookies=False))


def pack(pid, name, brand, set_id, price, slots, vintage=False, cond=(7.0, 10.0), desc=""):
    return {"id": pid, "name": name, "brandId": brand, "setId": set_id, "type": "Pack", "priceCredits": price,
            "isVintage": vintage, "artKey": pid, "description": desc, "slots": slots,
            "minCondition": cond[0], "maxCondition": cond[1]}


def box(pid, name, brand, set_id, price, contains, count, vintage=False, kind="Box", desc=""):
    return {"id": pid, "name": name, "brandId": brand, "setId": set_id, "type": kind, "priceCredits": price,
            "isVintage": vintage, "artKey": pid, "description": desc, "containsProductId": contains, "containsCount": count}


def opt(weight, rarities=(), parallel=None, rookie=False, auto=False, alt=False):
    o = {"weight": weight, "rarities": list(rarities), "rookieOnly": rookie, "autographOnly": auto, "alternateArtOnly": alt}
    if parallel:
        o["parallelId"] = parallel
    return o


products = [
    pack("fh26-pack", "Fieldhouse 2026 Hobby Pack", "fieldhouse", "fh26", 120, [
        {"count": 4, "options": [opt(70, ["Common"]), opt(30, ["Uncommon"])]},
        {"count": 1, "options": [opt(75, ["Rare"]), opt(20, ["SuperRare"]), opt(5, ["Legendary"])]},
        {"count": 1, "options": [opt(70, ["Common", "Uncommon"], rookie=True), opt(22, [], "silver"), opt(7.5, [], "gold50"), opt(0.5, [], "black1")]},
    ], desc="6 cards. One rookie or parallel per pack."),
    box("fh26-box", "Fieldhouse 2026 Hobby Box", "fieldhouse", "fh26", 1300, "fh26-pack", 12, kind="HobbyBox"),
    pack("apex26-pack", "Apex Diamond 2026 Pack", "apex-diamond", "apex26", 450, [
        {"count": 3, "options": [opt(60, ["Common"]), opt(40, ["Uncommon", "Rare"])]},
        {"count": 1, "options": [opt(55, ["SuperRare", "Legendary"], auto=True), opt(30, [], "gold25"), opt(12, [], "red10"), opt(3, [], "superfractor1")]},
    ], cond=(8.0, 10.0), desc="4 cards with one autograph or numbered refractor."),
    box("apex26-box", "Apex Diamond 2026 Hobby Box", "apex-diamond", "apex26", 1700, "apex26-pack", 4, kind="HobbyBox"),
    pack("hl94-wax", "Heritage Lane 1994 Wax Pack", "heritage-lane", "hl94", 300, [
        {"count": 5, "options": [opt(65, ["Common"]), opt(25, ["Uncommon"]), opt(10, ["Rare", "SuperRare", "Legendary"])]},
        {"count": 1, "options": [opt(85, ["Common", "Uncommon"]), opt(15, [], "foil94")]},
    ], vintage=True, cond=(3.0, 8.5), desc="Discontinued 1994 wax. Gum stain not included."),
    box("hl94-box", "Heritage Lane 1994 Wax Box", "heritage-lane", "hl94", 3200, "hl94-wax", 12, vintage=True),
    pack("fhr26-pack", "Rising Rookies Blaster Pack", "fieldhouse", "fhr26", 160, [
        {"count": 4, "options": [opt(80, ["Common", "Uncommon"]), opt(20, ["Rare", "SuperRare", "Legendary"])]},
        {"count": 1, "options": [opt(80, [], "silver"), opt(20, [], "gold50")]},
    ], desc="5 rookies including one parallel."),
    pack("aeth-booster", "Aetherfall Booster", "mythforge", "aeth", 100, [
        {"count": 6, "options": [opt(65, ["Common"]), opt(35, ["Uncommon"])]},
        {"count": 1, "options": [opt(70, ["Rare"]), opt(22, ["SuperRare"]), opt(8, ["Legendary"])]},
        {"count": 1, "options": [opt(68, [], "holo"), opt(20, [], None, alt=True), opt(11.5, [], "prismatic99"), opt(0.5, [], "mythic1")]},
    ], desc="8 cards with a guaranteed holo, alternate art or Prismatic."),
    box("aeth-box", "Aetherfall Booster Box", "mythforge", "aeth", 2200, "aeth-booster", 24),
    pack("nova-booster", "Nova Reach Booster", "starbound", "nova", 110, [
        {"count": 6, "options": [opt(65, ["Common"]), opt(35, ["Uncommon"])]},
        {"count": 1, "options": [opt(75, ["Rare"]), opt(20, ["SuperRare"]), opt(5, ["Legendary"])]},
        {"count": 1, "options": [opt(75, [], "holo"), opt(18, [], None, alt=True), opt(7, [], "starlight25")]},
    ]),
    box("nova-tin", "Nova Reach Collector Tin", "starbound", "nova", 500, "nova-booster", 5, kind="Tin"),
    pack("mfo01-booster", "Mythforge Origins 1st Print Booster", "mythforge", "mfo01", 650, [
        {"count": 7, "options": [opt(60, ["Common"]), opt(30, ["Uncommon"]), opt(10, ["Rare", "SuperRare", "Legendary"])]},
        {"count": 1, "options": [opt(70, ["Rare", "SuperRare", "Legendary"]), opt(30, [], "first-edition")]},
    ], vintage=True, cond=(4.0, 9.0), desc="Sealed since 2001."),
]

npcs = [
    {"id": "gus-tan", "name": "Gus Tanaka", "persona": "Old-school dealer who remembers every price guide.", "specialty": "Vintage sports",
     "sellMarkup": 1.25, "buyFactor": 0.65, "focusCategory": "Sports", "dealsVintage": True},
    {"id": "rosa-vale", "name": "Rosa Vale", "persona": "Modern breaker with an eye for rookies.", "specialty": "Modern sports",
     "sellMarkup": 1.12, "buyFactor": 0.72, "focusCategory": "Sports", "dealsVintage": False},
    {"id": "milo-crane", "name": "Milo Crane", "persona": "Runs the local game store and judges weekend tournaments.", "specialty": "TCG singles",
     "sellMarkup": 1.10, "buyFactor": 0.70, "focusCategory": "Tcg", "dealsVintage": False},
    {"id": "ada-quill", "name": "Ada Quill", "persona": "Collector-archivist of first prints.", "specialty": "Vintage TCG",
     "sellMarkup": 1.30, "buyFactor": 0.68, "focusCategory": "Tcg", "dealsVintage": True},
    {"id": "vic-sterling", "name": "Victor Sterling", "persona": "High-end consignment broker. Only the best.", "specialty": "Premium and graded",
     "sellMarkup": 1.35, "buyFactor": 0.78, "dealsVintage": True},
]


def cos(cid, name, kind, color="#333333", accent="#E3B341", default=False, desc=""):
    return {"id": cid, "name": name, "kind": kind, "colorHex": color, "accentHex": accent, "unlockedByDefault": default, "description": desc}


cosmetics = [
    cos("frame-classic", "Classic Frame", "CardFrame", "#E6E6E6", "#9A9A9A", True),
    cos("frame-gold", "Gilded Frame", "CardFrame", "#E3B341", "#7A5C12"),
    cos("frame-set-master", "Set Master Frame", "CardFrame", "#2D2D2D", "#E3B341"),
    cos("frame-expo-2026", "Expo 2026 Frame", "CardFrame", "#FF7A1A", "#1C1C1C"),
    cos("frame-runic", "Runic Frame", "CardFrame", "#3D2A5C", "#A6E3F0"),
    cos("frame-vintage", "Woodgrain Frame", "CardFrame", "#6B4A2B", "#D9B98C"),
    cos("bg-modern-luxury", "Modern Luxury", "ShowcaseBackground", "#1B1D22", "#C9A227", True),
    cos("bg-vintage-hobby-shop", "Vintage Hobby Shop", "ShowcaseBackground", "#4A3424", "#E8C99B"),
    cos("bg-dark-vault", "Dark Collector Vault", "ShowcaseBackground", "#0B0E14", "#3A7BD5"),
    cos("bg-fantasy-gallery", "Fantasy Gallery", "ShowcaseBackground", "#22163A", "#B57EDC"),
    cos("bg-scifi-archive", "Sci-Fi Archive", "ShowcaseBackground", "#0D1B2A", "#2F80ED"),
    cos("bg-minimalist", "Minimalist Showroom", "ShowcaseBackground", "#EEEEEE", "#222222"),
    cos("binder-standard", "Standard Binder", "BinderTheme", "#2B2B2B", "#666666", True),
    cos("binder-leather", "Leather Binder", "BinderTheme", "#5A3A22", "#C9A227"),
    cos("binder-expo", "Expo Binder", "BinderTheme", "#FF7A1A", "#222222"),
    cos("ped-basic", "Acrylic Stand", "DisplayPedestal", "#CCCCCC", "#FFFFFF", True),
    cos("ped-marble", "Marble Pedestal", "DisplayPedestal", "#E8E4DC", "#9C9486"),
    cos("ped-velvet", "Velvet Plinth", "DisplayPedestal", "#5C1A2B", "#E3B341"),
    cos("ped-crystal", "Crystal Pedestal", "DisplayPedestal", "#BDE6F5", "#FFFFFF"),
    cos("shelf-oak", "Oak Shelf", "ShelfStyle", "#7A5230", "#4A3020", True),
    cos("shelf-glass", "Glass Shelf", "ShelfStyle", "#9FD3E6", "#FFFFFF"),
    cos("wall-plaster", "Plaster Wall", "WallStyle", "#2A2C31", "#3A3D44", True),
    cos("wall-walnut", "Walnut Paneling", "WallStyle", "#4B3221", "#6B4A2B"),
    cos("fx-spotlight", "Spotlight", "DecorativeEffect", "#FFF6D5", "#FFFFFF", True),
    cos("fx-sparkle", "Foil Sparkle", "DecorativeEffect", "#FFFFFF", "#E3B341"),
    cos("title-newcomer", "Newcomer", "ProfileTitle", default=True),
    cos("title-rookie-hunter", "Rookie Hunter", "ProfileTitle"),
    cos("title-set-master", "Set Master", "ProfileTitle"),
    cos("title-vintage-curator", "Vintage Curator", "ProfileTitle"),
    cos("title-convention-regular", "Convention Regular", "ProfileTitle"),
    cos("title-one-of-one", "Keeper of the One", "ProfileTitle"),
    cos("title-grader", "Slab Specialist", "ProfileTitle"),
    cos("title-trader", "Deal Maker", "ProfileTitle"),
    cos("title-release-tracker", "Release Day Regular", "ProfileTitle"),
]

# Release calendar. New games start on game day 45 (see economy.startGameDay).
def rel(rid, name, brand, cat, set_id, announce, release, retire, archive, prods, desc, themes=(), chase=(), events=(), art=None):
    return {"id": rid, "name": name, "brandId": brand, "category": cat, "setId": set_id, "description": desc,
            "artKey": art or rid, "announceAt": announce * DAY, "releaseAt": release * DAY,
            "retireAt": retire * DAY if retire else 0, "archiveAt": archive * DAY if archive else 0,
            "products": [{"productId": p, "initialStock": s, "vintageAllocationOnRetire": v} for p, s, v in prods],
            "themes": list(themes), "chaseTemplateIds": list(chase), "eventIds": list(events)}


releases = [
    rel("rel-hl94", "Heritage Lane 1994 Original Run", "heritage-lane", "SportsFlagship", "hl94", 0, 1, 3, 5,
        [("hl94-wax", 20, 6), ("hl94-box", 2, 1)], "The 1994 run, long out of print. Leftover wax trades on the vintage market.",
        themes=("Vintage", "Baseball"), chase=("hl94-012",)),
    rel("rel-fh26", "Fieldhouse 2026 Hoops", "fieldhouse", "SportsFlagship", "fh26", 0, 14, 150, 200,
        [("fh26-pack", 400, 20), ("fh26-box", 40, 4)], "The flagship hoops release of the season.",
        themes=("Basketball", "Rookies"), chase=("fh26-020", "fh26-018"), events=("cs-sports-expo",)),
    rel("rel-aeth", "Mythforge: Aetherfall", "mythforge", "TcgExpansion", "aeth", 10, 30, 160, 220,
        [("aeth-booster", 500, 25), ("aeth-box", 30, 3)], "Storm dragons, rune wardens and alternate-art chase cards.",
        themes=("Fantasy", "Dragons"), chase=("aeth-020", "aeth-016"), events=("cs-tcg-convention",)),
    rel("rel-mfo-anniv", "Mythforge 25th Anniversary Restock", "mythforge", "Anniversary", "mfo01", 35, 40, 75, 120,
        [("mfo01-booster", 24, 8)], "A tiny cache of 2001 first-print boosters, found in a warehouse.",
        themes=("Vintage", "Anniversary"), chase=("mfo01-012",), events=("cs-vintage-fair",)),
    rel("rel-fhr26", "Fieldhouse Rising Rookies", "fieldhouse", "SportsRookie", "fhr26", 40, 46, 60, 90,
        [("fhr26-pack", 60, 10)], "A short-print, rookie-only release with a two-week window.",
        themes=("Rookies",), chase=("fhr26-010",)),
    rel("rel-apex26", "Apex Diamond 2026 Baseball", "apex-diamond", "SportsPremium", "apex26", 44, 52, 180, 240,
        [("apex26-pack", 150, 10), ("apex26-box", 25, 3)], "On-card autographs and refractors numbered to 1.",
        themes=("Baseball", "Autographs"), chase=("apex26-012", "apex26-010"), events=("cs-premium-showcase",)),
    rel("rel-nova", "Starbound Saga: Nova Reach", "starbound", "TcgExpansion", "nova", 46, 62, 200, 260,
        [("nova-booster", 450, 20), ("nova-tin", 60, 5)], "A new frontier for TCG collectors.",
        themes=("Sci-Fi",), chase=("nova-016", "nova-015")),
    rel("rel-aeth-alt", "Aetherfall Alternate Art Collection", "mythforge", "AlternateArtCollection", "aeth", 50, 70, 98, 140,
        [("aeth-booster", 120, 0)], "A limited second wave focused on alternate-art pulls.",
        themes=("Alternate Art",), chase=("aeth-016", "aeth-018", "aeth-020")),
    rel("rel-hl94-restock", "Heritage Lane Vintage Restock", "heritage-lane", "VintageRestock", "hl94", 52, 58, 72, 100,
        [("hl94-wax", 12, 4)], "A dealer estate sale surfaces more 1994 wax.", themes=("Vintage",)),
    rel("rel-summer", "Summer Classic Seasonal Packs", "fieldhouse", "LimitedSeasonal", "fh26", 60, 80, 94, 130,
        [("fh26-pack", 80, 0)], "Seasonal limited run of Fieldhouse 2026 packs.", themes=("Seasonal",)),
    rel("rel-show-expo", "Sports Card Expo Weekend", "fieldhouse", "CardShowEvent", "fh26", 40, 45, 0, 0,
        [], "The weekly Sports Card Expo opens its doors.", events=("cs-sports-expo",)),
]


def stock(sid, product=None, template=None, parallel=None, grade=None, qty=1, credits=0, tickets=0, unlock=None, cond=9.0):
    s = {"id": sid, "quantity": qty, "priceCredits": credits, "priceTickets": tickets, "condition": cond}
    if product:
        s["productId"] = product
    if template:
        s["templateId"] = template
    if parallel:
        s["parallelId"] = parallel
    if grade is not None:
        s["grade"] = grade
    if unlock:
        s["requiresUnlockId"] = unlock
    return s


def ch(cid, desc, action, count, points, **f):
    flt = {"setIds": f.get("sets", []), "brandIds": f.get("brands", []), "distinctTemplates": f.get("distinct", False),
           "distinctBrands": f.get("distinct_brands", False), "featuredOnly": f.get("featured", False)}
    for k_src, k_dst in (("rookie", "rookie"), ("vintage", "vintage"), ("serial", "serialNumbered"), ("category", "category")):
        if k_src in f:
            flt[k_dst] = f[k_src]
    return {"id": cid, "description": desc, "action": action, "count": count, "points": points, "filter": flt}


card_shows = [
    {"id": "cs-daily-meetup", "name": "Daily Collector Meetup", "category": "DailyMiniEvent", "artKey": "cs-daily",
     "description": "A small daily swap meet at the hobby shop. Quick goals, quick rewards.",
     "schedule": {"kind": "Daily", "firstStartAt": 0, "durationSeconds": DAY},
     "featuredBrandIds": [], "featuredSetIds": [],
     "vendors": [{"id": "meetup-milo", "npcId": "milo-crane", "boothName": "Milo's Card Table",
                  "description": "Loose packs at a friendly price.",
                  "stock": [stock("meetup-aeth", product="aeth-booster", qty=3, credits=95),
                            stock("meetup-fh", product="fh26-pack", qty=3, credits=110)]}],
     "challenges": [ch("open2", "Open 2 packs", "PackOpened", 2, 10),
                    ch("acquire8", "Pull or buy 8 cards", "CardAcquired", 8, 10)],
     "rewards": [{"id": "meetup-credits", "name": "150 credits", "costPoints": 20, "reward": reward(credits=150)}],
     "completionReward": reward(xp=20),
     "pricing": {"vendorMarkup": 1.0, "featuredSetDiscount": 1.0}},
    {"id": "cs-sports-expo", "name": "Sports Card Expo", "category": "SportsCardExpo", "artKey": "cs-sports-expo",
     "description": "Three days of sports-card dealers, rookie hunts and the Expo Exclusive parallel.",
     "schedule": {"kind": "Weekly", "firstStartAt": 45 * DAY, "durationSeconds": 3 * DAY},
     "featuredBrandIds": ["fieldhouse", "apex-diamond"], "featuredSetIds": ["fh26", "fhr26"],
     "vendors": [
         {"id": "expo-rosa", "npcId": "rosa-vale", "boothName": "Rosa's Rookie Row", "description": "Fresh hobby boxes and rookie singles.",
          "stock": [stock("expo-fh-box", product="fh26-box", qty=2, credits=1150),
                    stock("expo-fh-pack", product="fh26-pack", qty=10, credits=105),
                    stock("expo-rookie-single", template="fh26-018", qty=2, credits=260),
                    stock("expo-park-gold", template="fh26-018", parallel="gold50", qty=1, credits=2400)]},
         {"id": "expo-gus", "npcId": "gus-tan", "boothName": "Gus's Vintage Corner", "description": "1994 wax and graded vintage.",
          "stock": [stock("expo-hl-wax", product="hl94-wax", qty=2, credits=420),
                    stock("expo-hl-graded", template="hl94-012", grade=8.0, cond=8.0, qty=1, credits=1600)]},
     ],
     "challenges": [ch("open5", "Open 5 packs from Fieldhouse 2026", "PackOpened", 5, 20, sets=["fh26"]),
                    ch("rookies3", "Acquire 3 different rookie cards", "CardAcquired", 3, 20, rookie=True, distinct=True),
                    ch("grade1", "Grade an eligible card", "CardGraded", 1, 25),
                    ch("trade1", "Complete an NPC trade", "TradeCompleted", 1, 25),
                    ch("brands2", "Acquire cards from 2 featured brands", "CardAcquired", 2, 20, featured=True, distinct_brands=True)],
     "rewards": [{"id": "expo-frame", "name": "Expo 2026 Frame", "costPoints": 30, "reward": reward(cosmetics=["frame-expo-2026"])},
                 {"id": "expo-binder", "name": "Expo Binder Theme", "costPoints": 20, "reward": reward(cosmetics=["binder-expo"])},
                 {"id": "expo-tickets", "name": "2 Event Tickets", "costPoints": 25, "reward": reward(tickets=2)},
                 {"id": "expo-exclusive", "name": "Expo Exclusive Mara Voss /25", "costPoints": 60,
                  "reward": reward(cards=[card_grant("fh26-001", "expo25", tradable=True, note="Expo Exclusive")])},
                 {"id": "expo-commemorative", "name": "Commemorative Expo Card", "costPoints": 15,
                  "reward": reward(cards=[card_grant("fh26-002", None, tradable=False, note="Commemorative, non-tradable")])}],
     "completionReward": reward(credits=500, cosmetics=["title-convention-regular"], xp=100),
     "pricing": {"vendorMarkup": 1.0, "featuredSetDiscount": 0.9}},
    {"id": "cs-tcg-convention", "name": "TCG Collector Convention", "category": "TcgCollectorConvention", "artKey": "cs-tcg",
     "description": "Featured universes, alternate-art showcases and themed set challenges.",
     "schedule": {"kind": "Monthly", "firstStartAt": 48 * DAY, "durationSeconds": 4 * DAY},
     "featuredBrandIds": ["mythforge", "starbound"], "featuredSetIds": ["aeth", "nova"],
     "vendors": [
         {"id": "con-milo", "npcId": "milo-crane", "boothName": "Crane's Game Vault", "description": "Boosters and alternate-art singles.",
          "stock": [stock("con-aeth-box", product="aeth-box", qty=1, credits=2000),
                    stock("con-aeth-booster", product="aeth-booster", qty=12, credits=95),
                    stock("con-alt-vael", template="aeth-016", qty=1, credits=900),
                    stock("con-alt-isolde", template="aeth-018", qty=1, credits=950)]},
         {"id": "con-ada", "npcId": "ada-quill", "boothName": "Quill & Archive", "description": "First prints from 2001.",
          "stock": [stock("con-mfo", product="mfo01-booster", qty=2, credits=900),
                    stock("con-mythic-promo", template="aeth-001", parallel="prismatic99", qty=1, tickets=3)]},
     ],
     "challenges": [ch("acq5", "Acquire 5 different cards from featured sets", "CardAcquired", 5, 20, featured=True, distinct=True),
                    ch("open3", "Open 3 featured boosters", "PackOpened", 3, 20, featured=True),
                    ch("buy1", "Purchase a sealed product", "ProductPurchased", 1, 15),
                    ch("milestone1", "Reach a set milestone", "SetMilestoneReached", 1, 25, category="Tcg")],
     "rewards": [{"id": "con-gallery", "name": "Fantasy Gallery Showcase", "costPoints": 40, "reward": reward(cosmetics=["bg-fantasy-gallery"])},
                 {"id": "con-runic", "name": "Runic Frame", "costPoints": 25, "reward": reward(cosmetics=["frame-runic"])},
                 {"id": "con-archive", "name": "Sci-Fi Archive Showcase", "costPoints": 40, "reward": reward(cosmetics=["bg-scifi-archive"])},
                 {"id": "con-promo", "name": "Convention Promo: Aether Phoenix", "costPoints": 30,
                  "reward": reward(cards=[card_grant("aeth-008", "holo", tradable=False, note="Convention promo")])}],
     "completionReward": reward(credits=600, tickets=1, xp=120),
     "pricing": {"vendorMarkup": 1.0, "featuredSetDiscount": 0.9}},
    {"id": "cs-vintage-fair", "name": "Vintage Card Fair", "category": "VintageCardFair", "artKey": "cs-vintage",
     "description": "Discontinued wax, historical TCG boosters and graded vintage in limited quantities.",
     "schedule": {"kind": "Monthly", "firstStartAt": 55 * DAY, "durationSeconds": 3 * DAY},
     "featuredBrandIds": ["heritage-lane", "mythforge"], "featuredSetIds": ["hl94", "mfo01"],
     "vendors": [
         {"id": "fair-gus", "npcId": "gus-tan", "boothName": "Tanaka Collectibles", "description": "Wax boxes from 1994.",
          "stock": [stock("fair-hl-box", product="hl94-box", qty=1, credits=3400),
                    stock("fair-hl-wax", product="hl94-wax", qty=3, credits=320),
                    stock("fair-hl-graded", template="hl94-004", grade=9.0, cond=9.0, qty=1, credits=1200)]},
         {"id": "fair-ada", "npcId": "ada-quill", "boothName": "Quill & Archive", "description": "First prints, sealed.",
          "stock": [stock("fair-mfo", product="mfo01-booster", qty=3, credits=700),
                    stock("fair-mfo-graded", template="mfo01-012", grade=8.5, cond=8.5, qty=1, credits=4200, unlock="vintage-vip")]},
     ],
     "challenges": [ch("buyvintage", "Purchase one vintage product", "ProductPurchased", 1, 20, vintage=True),
                    ch("openvintage", "Open a vintage pack", "PackOpened", 1, 20, vintage=True),
                    ch("vintage3", "Acquire 3 different vintage cards", "CardAcquired", 3, 20, vintage=True, distinct=True)],
     "rewards": [{"id": "fair-shop", "name": "Vintage Hobby Shop Showcase", "costPoints": 30, "reward": reward(cosmetics=["bg-vintage-hobby-shop"])},
                 {"id": "fair-frame", "name": "Woodgrain Frame", "costPoints": 20, "reward": reward(cosmetics=["frame-vintage"])},
                 {"id": "fair-vip", "name": "Vintage VIP Access", "costPoints": 20, "reward": reward(unlocks=["vintage-vip"])}],
     "completionReward": reward(credits=400, cosmetics=["wall-walnut"], xp=100),
     "pricing": {"vendorMarkup": 1.0, "featuredSetDiscount": 1.0}},
    {"id": "cs-premium-showcase", "name": "Premium Collector Showcase", "category": "PremiumCollectorShowcase", "artKey": "cs-premium",
     "description": "High-end numbered parallels, autographs and rare graded cards. Strictly optional luxury.",
     "schedule": {"kind": "Monthly", "firstStartAt": 60 * DAY, "durationSeconds": 2 * DAY},
     "featuredBrandIds": ["apex-diamond"], "featuredSetIds": ["apex26"],
     "vendors": [
         {"id": "prem-vic", "npcId": "vic-sterling", "boothName": "Sterling Consignment", "description": "Autographs and refractors.",
          "stock": [stock("prem-auto", template="apex26-010", qty=1, credits=5200),
                    stock("prem-red", template="apex26-001", parallel="red10", qty=1, credits=6000),
                    stock("prem-graded", template="apex26-012", grade=9.5, cond=9.5, qty=1, credits=9500),
                    stock("prem-ticket-box", product="apex26-box", qty=1, tickets=4)]},
     ],
     "challenges": [ch("serial1", "Acquire a serial-numbered card", "CardAcquired", 1, 25, serial=True),
                    ch("grade1", "Grade an eligible card", "CardGraded", 1, 20),
                    ch("open-apex", "Open an Apex Diamond pack", "PackOpened", 1, 15, sets=["apex26"])],
     "rewards": [{"id": "prem-velvet", "name": "Velvet Plinth", "costPoints": 25, "reward": reward(cosmetics=["ped-velvet"])},
                 {"id": "prem-title", "name": "Slab Specialist Title", "costPoints": 20, "reward": reward(cosmetics=["title-grader"])},
                 {"id": "prem-vault", "name": "Dark Collector Vault", "costPoints": 30, "reward": reward(cosmetics=["bg-dark-vault"])}],
     "completionReward": reward(credits=750, xp=150),
     "pricing": {"vendorMarkup": 1.05, "featuredSetDiscount": 1.0}},
]

showcase = {
    "defaultBackgroundId": "bg-modern-luxury", "defaultFrameId": "frame-classic", "defaultPedestalId": "ped-basic",
    "defaultShelfId": "shelf-oak", "defaultWallId": "wall-plaster",
    "areas": [
        {"id": "featured", "name": "Featured Pedestal", "kind": "FeaturedPedestal", "baseSlots": 1},
        {"id": "main-wall", "name": "Main Display Wall", "kind": "MainWall", "baseSlots": 4,
         "extraSlots": [{"slots": 2, "condition": {"kind": "CollectorRank", "value": 3}},
                        {"slots": 2, "condition": {"kind": "CollectorRank", "value": 6}}]},
        {"id": "graded-shelf", "name": "Graded Slab Shelf", "kind": "GradedShelf", "baseSlots": 3,
         "areaUnlock": {"kind": "CardsGraded", "value": 1},
         "extraSlots": [{"slots": 3, "condition": {"kind": "CardsGraded", "value": 10}}]},
        {"id": "vintage-cabinet", "name": "Vintage Cabinet", "kind": "VintageCabinet", "baseSlots": 3,
         "areaUnlock": {"kind": "VintageProductsOpened", "value": 1},
         "extraSlots": [{"slots": 3, "condition": {"kind": "Achievement", "refId": "ach-vintage-5"}}]},
        {"id": "sports", "name": "Sports Section", "kind": "SportsSection", "baseSlots": 4},
        {"id": "tcg", "name": "TCG Section", "kind": "TcgSection", "baseSlots": 4},
        {"id": "completed-sets", "name": "Completed Sets", "kind": "CompletedSets", "baseSlots": 3,
         "areaUnlock": {"kind": "SetsCompleted", "value": 1}},
        {"id": "one-of-one", "name": "One-of-One Vault", "kind": "OneOfOne", "baseSlots": 1,
         "areaUnlock": {"kind": "Achievement", "refId": "ach-first-one-of-one"},
         "extraSlots": [{"slots": 2, "condition": {"kind": "CollectorRank", "value": 8}}]},
        {"id": "favorites", "name": "Personal Favorites", "kind": "Favorites", "baseSlots": 3,
         "extraSlots": [{"slots": 3, "condition": {"kind": "CardShowsCompleted", "value": 1}}]},
    ],
}


def ach(aid, name, desc, stat, threshold, rwd, hidden=False):
    return {"id": aid, "name": name, "description": desc, "stat": stat, "threshold": threshold, "reward": rwd, "hidden": hidden}


achievements = [
    ach("ach-first-pack", "First Rip", "Open your first pack.", "PacksOpened", 1, reward(credits=100, xp=20)),
    ach("ach-packs-50", "Pack Veteran", "Open 50 packs.", "PacksOpened", 50, reward(cosmetics=["fx-sparkle"], xp=100)),
    ach("ach-unique-25", "Growing Collection", "Own 25 different cards.", "UniqueCardsAcquired", 25, reward(credits=200, xp=40)),
    ach("ach-unique-100", "Serious Collector", "Own 100 different cards.", "UniqueCardsAcquired", 100, reward(cosmetics=["bg-minimalist"], xp=150)),
    ach("ach-first-set", "Set Builder", "Complete your first set.", "SetsCompleted", 1, reward(tickets=2, xp=100)),
    ach("ach-sets-3", "Registry Regular", "Complete three sets.", "SetsCompleted", 3, reward(cosmetics=["frame-gold"], xp=250)),
    ach("ach-vintage-1", "Time Capsule", "Open a vintage product.", "VintageProductsOpened", 1, reward(credits=150, xp=30)),
    ach("ach-vintage-5", "Wax Archaeologist", "Open 5 vintage products.", "VintageProductsOpened", 5, reward(cosmetics=["title-vintage-curator"], xp=120)),
    ach("ach-graded-1", "First Slab", "Grade a card.", "CardsGraded", 1, reward(xp=30)),
    ach("ach-graded-10", "Slab Stacker", "Grade 10 cards.", "CardsGraded", 10, reward(cosmetics=["title-grader"], xp=150)),
    ach("ach-trade-1", "Handshake Deal", "Complete an NPC trade.", "NpcTradesCompleted", 1, reward(credits=100, xp=30)),
    ach("ach-trade-10", "Deal Maker", "Complete 10 NPC trades.", "NpcTradesCompleted", 10, reward(cosmetics=["title-trader"], xp=150)),
    ach("ach-show-1", "Show Floor", "Complete a card show.", "CardShowsCompleted", 1, reward(tickets=1, xp=80)),
    ach("ach-show-challenges-10", "Challenge Seeker", "Complete 10 card show challenges.", "CardShowChallengesCompleted", 10, reward(credits=300, xp=100)),
    ach("ach-rare-1", "Big Hit", "Pull or buy a serial-numbered card.", "RareCardsDiscovered", 1, reward(xp=50)),
    ach("ach-first-one-of-one", "The One", "Own a genuine one-of-one.", "OneOfOnesDiscovered", 1, reward(cosmetics=["title-one-of-one"], xp=300), hidden=True),
    ach("ach-release-3", "Release Day Regular", "Buy on release week for 3 releases.", "ReleaseMilestones", 3, reward(cosmetics=["title-release-tracker"], xp=100)),
    ach("ach-showcase-3", "Curator", "Unlock 3 showcase upgrades.", "ShowcaseUpgradesUnlocked", 3, reward(cosmetics=["wall-walnut"], xp=80)),
    ach("ach-history-1", "Storied Card", "Own a card that has passed through three owners.", "HistoricalMilestones", 1, reward(xp=60)),
]

ranks = [
    {"level": 1, "name": "Newcomer", "xpRequired": 0, "reward": reward()},
    {"level": 2, "name": "Hobbyist", "xpRequired": 150, "reward": reward(credits=200)},
    {"level": 3, "name": "Collector", "xpRequired": 400, "reward": reward(tickets=1)},
    {"level": 4, "name": "Enthusiast", "xpRequired": 800, "reward": reward(credits=400)},
    {"level": 5, "name": "Curator", "xpRequired": 1400, "reward": reward(cosmetics=["shelf-glass"])},
    {"level": 6, "name": "Connoisseur", "xpRequired": 2200, "reward": reward(tickets=2)},
    {"level": 7, "name": "Archivist", "xpRequired": 3200, "reward": reward(credits=800)},
    {"level": 8, "name": "Vault Keeper", "xpRequired": 4500, "reward": reward(cosmetics=["bg-dark-vault"])},
    {"level": 9, "name": "Hall of Famer", "xpRequired": 6200, "reward": reward(tickets=3)},
    {"level": 10, "name": "Legend", "xpRequired": 8500, "reward": reward(cosmetics=["frame-gold"])},
]

content = {
    "contentVersion": 1,
    "calendarEpochUtc": "2026-01-01T00:00:00Z",
    "brands": brands, "universes": universes, "leagues": leagues, "teams": teams, "subjects": subjects,
    "parallels": parallels, "cards": cards, "sets": sets, "products": products, "npcs": npcs, "cosmetics": cosmetics,
    "releases": releases, "cardShows": card_shows, "achievements": achievements, "collectorRanks": ranks,
    "showcase": showcase,
    "grading": {"feeCredits": 150, "turnaroundSeconds": 2 * DAY, "minimumGrade": 1, "maximumGrade": 10},
    "market": {"dailyListingCount": 10, "vintageListingCount": 4, "listingLifetimeDays": 3, "dailyTradeOffers": 2,
               "playerListingSaleChancePerDay": 0.5, "maxSellPriceOverValue": 1.5},
    "economy": {"startingCredits": 2500, "startingEventTickets": 0, "startGameDay": 45,
                "startingProductIds": ["fh26-pack", "fh26-pack", "aeth-booster", "aeth-booster"]},
    "xp": {"perNewUniqueCard": 5, "perPackOpened": 2, "perCardGraded": 10, "perTrade": 15, "perSetMilestone": 40,
           "perChallenge": 20, "perRareDiscovery": 25},
}

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(content, f, indent=1, ensure_ascii=False)
    f.write("\n")
print("Wrote %s: %d cards, %d sets, %d products, %d releases, %d card shows" %
      (os.path.normpath(OUT), len(cards), len(sets), len(products), len(releases), len(card_shows)))
