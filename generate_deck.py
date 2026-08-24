# -*- coding: utf-8 -*-
"""
generate_deck.py — Deck commercial & institutionnel SamaEcole / Unikol.

Genere « Presentation_Unikol_2026.pptx » a la racine du projet : 15 slides 16:9
destinees aux directeurs d'ecole, promoteurs prives et autorites educatives.

    python -m pip install python-pptx
    python generate_deck.py

Le contenu n'est PAS inventé : il est tiré du code de ce dépôt (modules livrés,
documents PDF réellement générés, rôles, formules d'abonnement, matrice
`PlanFeatures`) et de `ACTIVE_CONTEXT.md`. La palette suit
`src/SamaEcole.Web/tailwind.config.js` (primary #6366F1) — voir CHARTE ci-dessous.

ATTENTION — les tarifs de la slide 14 proviennent de la section
`SubscriptionPricing` de `src/SamaEcole.Web/appsettings.json`, explicitement
marquée PLACEHOLDER dans le dépôt. Ils sont affichés comme « indicatifs » et
doivent être validés avant toute diffusion commerciale (constante PRICING).
"""

from pptx import Presentation
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR, PP_ALIGN
from pptx.oxml import parse_xml
from pptx.oxml.ns import nsdecls, qn
from pptx.util import Inches, Pt

# --------------------------------------------------------------------------------------
# CHARTE — alignée sur src/SamaEcole.Web/tailwind.config.js (§ Volume_5_UIUX_Design.md)
# --------------------------------------------------------------------------------------
BLEU_ROI = "4F46E5"   # primary-600 — couleur d'action
INDIGO = "6366F1"     # primary DEFAULT (dashboard-reference.jpg fait foi)
INDIGO_DEEP = "312E81" # primary-900 — fonds pleins
VIOLET = "7C3AED"     # accent violet de la charte Unikol
VIOLET_LIGHT = "A78BFA"

INK = "0F172A"        # titres
BODY = "475569"       # texte courant
MUTED = "94A3B8"      # legendes, pieds de page
LINE = "E2E8F0"       # filets et bordures de carte
CARD_BG = "F8FAFC"    # fond gris clair des cartes
TINT = "EEF2FF"       # primary-50 — fonds teintes
WHITE = "FFFFFF"

SUCCESS = "1E8E3E"
WARNING = "E8710A"
DANGER = "D93025"

FONT = "Segoe UI"     # repli documenté d'Inter (Volume 5 §2.2) — présent sur tout poste Windows

# Geometrie 16:9
SW, SH = 13.333, 7.5
ML = 0.75                      # marge gauche/droite
CW = SW - 2 * ML               # largeur utile : 11.833"
CONTENT_TOP = 2.12
FOOTER_Y = 7.02

OUTPUT = "Presentation_Unikol_2026.pptx"


# --------------------------------------------------------------------------------------
# Primitives de dessin
# --------------------------------------------------------------------------------------
def In(v):
    return Inches(v)


def style_run(run, size, color, bold=False, spacing=None, italic=False, font=FONT):
    """Applique police/taille/couleur. `spacing` = interlettrage en centiemes de point."""
    f = run.font
    f.name = font
    f.size = Pt(size)
    f.bold = bold
    f.italic = italic
    f.color.rgb = RGBColor.from_string(color)
    if spacing:
        f._rPr.set("spc", str(int(spacing)))
    return run


def lighten(hex_color, amount=0.30):
    """Melange vers le blanc — sert aux degrades de badge, qui doivent rester dans leur teinte."""
    r, g, b = (int(hex_color[i:i + 2], 16) for i in (0, 2, 4))
    return "%02X%02X%02X" % tuple(int(round(c + (255 - c) * amount)) for c in (r, g, b))


def apply_bullet(paragraph, char, color, mar_l=0.16):
    """Puce reelle PowerPoint (buChar) : donne un retrait pendant propre au retour a la ligne."""
    pPr = paragraph._p.get_or_add_pPr()
    pPr.set("marL", str(int(In(mar_l))))
    pPr.set("indent", str(int(-In(mar_l))))
    pPr.append(parse_xml('<a:buClr %s><a:srgbClr val="%s"/></a:buClr>' % (nsdecls("a"), color)))
    pPr.append(parse_xml('<a:buFont %s typeface="Arial"/>' % nsdecls("a")))
    pPr.append(parse_xml('<a:buChar %s char="%s"/>' % (nsdecls("a"), char)))


def textbox(slide, x, y, w, h, anchor=MSO_ANCHOR.TOP):
    box = slide.shapes.add_textbox(In(x), In(y), In(w), In(h))
    tf = box.text_frame
    tf.word_wrap = True
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = 0
    tf.vertical_anchor = anchor
    return tf


def para(tf, text, size=11, color=BODY, bold=False, align=PP_ALIGN.LEFT,
         space_before=0, space_after=0, line_spacing=1.2, spacing=None,
         italic=False, bullet=None, bullet_color=None, font=FONT):
    """Ajoute un paragraphe ; reutilise le premier s'il est encore vide."""
    if len(tf.paragraphs) == 1 and not tf.paragraphs[0].runs:
        p = tf.paragraphs[0]
    else:
        p = tf.add_paragraph()
    p.alignment = align
    p.line_spacing = line_spacing
    p.space_before = Pt(space_before)
    p.space_after = Pt(space_after)
    if bullet:
        apply_bullet(p, bullet, bullet_color or color)
    style_run(p.add_run(), size, color, bold, spacing, italic, font)
    p.runs[-1].text = text
    return p


def _clear_fills(spPr):
    for tag in ("a:noFill", "a:solidFill", "a:gradFill", "a:blipFill", "a:pattFill", "a:grpFill"):
        for el in spPr.findall(qn(tag)):
            spPr.remove(el)


def gradient(shape, c1, c2, angle=45):
    """Degrade lineaire pose en XML : deterministe, contrairement au nombre de stops par defaut."""
    spPr = shape._element.spPr
    _clear_fills(spPr)
    frag = parse_xml(
        '<a:gradFill %s rotWithShape="1"><a:gsLst>'
        '<a:gs pos="0"><a:srgbClr val="%s"/></a:gs>'
        '<a:gs pos="100000"><a:srgbClr val="%s"/></a:gs>'
        '</a:gsLst><a:lin ang="%d" scaled="0"/></a:gradFill>'
        % (nsdecls("a"), c1, c2, int(angle * 60000)))
    spPr.insert_element_before(frag, "a:ln", "a:effectLst", "a:effectDag",
                               "a:scene3d", "a:sp3d", "a:extLst")


def set_alpha(shape, percent):
    srgb = shape._element.spPr.find(qn("a:solidFill")).find(qn("a:srgbClr"))
    srgb.append(parse_xml('<a:alpha %s val="%d"/>' % (nsdecls("a"), int(percent * 1000))))


def shape(slide, x, y, w, h, fill=None, line=None, kind=MSO_SHAPE.RECTANGLE,
          radius=None, alpha=None, line_w=1.0, grad=None, grad_angle=45):
    sp = slide.shapes.add_shape(kind, In(x), In(y), In(w), In(h))
    style_el = sp._element.find(qn("p:style"))
    if style_el is not None:
        sp._element.remove(style_el)
    sp.shadow.inherit = False                      # sinon l'ombre du theme salit chaque carte
    if radius is not None and kind == MSO_SHAPE.ROUNDED_RECTANGLE:
        sp.adjustments[0] = radius
    if grad:
        gradient(sp, grad[0], grad[1], grad_angle)
    elif fill:
        sp.fill.solid()
        sp.fill.fore_color.rgb = RGBColor.from_string(fill)
        if alpha is not None:
            set_alpha(sp, alpha)
    else:
        sp.fill.background()
    if line:
        sp.line.color.rgb = RGBColor.from_string(line)
        sp.line.width = Pt(line_w)
    else:
        sp.line.fill.background()
    sp.text_frame.word_wrap = True
    return sp


def new_slide(prs, bg=WHITE):
    slide = prs.slides.add_slide(prs.slide_layouts[6])
    shape(slide, 0, 0, SW, SH, fill=bg)
    return slide


# --------------------------------------------------------------------------------------
# Composants
# --------------------------------------------------------------------------------------
def header(slide, eyebrow, title, lede=None, accent=BLEU_ROI):
    """Bandeau superieur : filet degrade, sur-titre, titre, chapeau."""
    shape(slide, 0, 0, SW, 0.115, grad=(BLEU_ROI, VIOLET), grad_angle=0)
    tf = textbox(slide, ML, 0.58, CW, 0.28)
    para(tf, eyebrow.upper(), size=9.5, color=accent, bold=True, spacing=180)
    tf = textbox(slide, ML, 0.90, CW, 0.62)
    para(tf, title, size=27, color=INK, bold=True, line_spacing=1.05)
    if lede:
        tf = textbox(slide, ML, 1.56, CW - 1.2, 0.42)
        para(tf, lede, size=12.5, color=BODY, line_spacing=1.25)


def footer(slide, number):
    shape(slide, ML, FOOTER_Y - 0.16, CW, 0.012, fill=LINE)
    tf = textbox(slide, ML, FOOTER_Y, CW * 0.7, 0.24)
    para(tf, "SamaEcole · Unikol  —  Système d'Information Éducatif", size=8.5, color=MUTED)
    tf = textbox(slide, ML + CW - 1.2, FOOTER_Y, 1.2, 0.24)
    para(tf, "%02d" % number, size=8.5, color=MUTED, bold=True, align=PP_ALIGN.RIGHT)


def badge(slide, x, y, size, label, grad_colors=(BLEU_ROI, VIOLET), font_size=13):
    sp = shape(slide, x, y, size, size, kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.22,
               grad=grad_colors)
    tf = sp.text_frame
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = 0
    tf.vertical_anchor = MSO_ANCHOR.MIDDLE
    para(tf, label, size=font_size, color=WHITE, bold=True, align=PP_ALIGN.CENTER, line_spacing=1.0)
    return sp


def card(slide, x, y, w, h, title, bullets, num=None, accent=BLEU_ROI,
         fill=CARD_BG, border=LINE, title_size=13, body_size=10.5, pad=0.30):
    """Carte : fond gris clair, filet fin, badge numerote optionnel, puces a retrait pendant."""
    shape(slide, x, y, w, h, fill=fill, line=border, kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.06)
    cy = y + pad
    if num is not None:
        grad = (accent, VIOLET) if accent == BLEU_ROI else (accent, lighten(accent))
        badge(slide, x + pad, cy, 0.42, str(num), grad, font_size=12.5)
        cy += 0.60
    # Hauteur du titre deduite du nombre de lignes reelles : ~0.56 em de large par caractere
    # en Segoe UI. Sans cela, un titre sur deux lignes recouvre la premiere puce.
    per_line = max(1, int((w - 2 * pad) * 96 / (title_size * 0.56)))
    n_lines = max(1, -(-len(title) // per_line))
    line_h = 0.26 if title_size <= 12 else 0.31
    tf = textbox(slide, x + pad, cy, w - 2 * pad, line_h * n_lines + 0.10)
    para(tf, title, size=title_size, color=INK, bold=True, line_spacing=1.1)
    cy += line_h * n_lines + 0.04
    tf = textbox(slide, x + pad, cy + 0.14, w - 2 * pad, h - (cy + 0.14 - y) - pad)
    for i, b in enumerate(bullets):
        para(tf, b, size=body_size, color=BODY, line_spacing=1.22,
             space_before=0 if i == 0 else 5.5, bullet="▪", bullet_color=accent)


def band(slide, x, y, w, h, text, label=None, accent=BLEU_ROI, size=11.5, fill=TINT):
    """Bandeau de conclusion en bas de slide — la phrase que le prospect doit retenir."""
    shape(slide, x, y, w, h, fill=fill, kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.10)
    shape(slide, x, y + 0.10, 0.055, h - 0.20, fill=accent)
    tf = textbox(slide, x + 0.34, y + 0.16, w - 0.68, h - 0.32, anchor=MSO_ANCHOR.MIDDLE)
    if label:
        para(tf, label.upper(), size=8.5, color=accent, bold=True, spacing=160, space_after=3)
    para(tf, text, size=size, color=INK, line_spacing=1.25)


def kpi(slide, x, y, w, h, value, label, accent=BLEU_ROI, unit=None, value_size=26):
    shape(slide, x, y, w, h, fill=WHITE, line=LINE, kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.08)
    shape(slide, x, y, w, 0.075, fill=accent)
    tf = textbox(slide, x + 0.26, y + 0.30, w - 0.52, 0.52)
    p = para(tf, value, size=value_size, color=INK, bold=True, line_spacing=1.0)
    if unit:
        style_run(p.add_run(), 12, accent, bold=True).text = " " + unit
    tf = textbox(slide, x + 0.26, y + 0.88, w - 0.52, 0.48)
    para(tf, label, size=9.5, color=BODY, line_spacing=1.2)


def chip(slide, x, y, w, h, label, accent=BLEU_ROI):
    shape(slide, x, y, w, h, fill=WHITE, line=LINE, kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.20)
    shape(slide, x + 0.20, y + h / 2 - 0.055, 0.11, 0.11, fill=accent, kind=MSO_SHAPE.OVAL)
    tf = textbox(slide, x + 0.42, y, w - 0.58, h, anchor=MSO_ANCHOR.MIDDLE)
    para(tf, label, size=9.5, color=INK, line_spacing=1.1)


def cols(n, gap=0.29):
    """Largeur et abscisses de n colonnes egales dans la zone utile."""
    w = (CW - gap * (n - 1)) / n
    return w, [ML + i * (w + gap) for i in range(n)]


# --------------------------------------------------------------------------------------
# SLIDES
# --------------------------------------------------------------------------------------
def slide_01_cover(prs):
    slide = new_slide(prs)
    shape(slide, 0, 0, SW, SH, grad=(INDIGO_DEEP, VIOLET), grad_angle=35)
    # Halos décoratifs : cercles blancs très faiblement opaques, hors zone de texte.
    for x, y, d, a in ((9.7, -1.7, 5.6, 9), (11.2, 4.5, 4.2, 7), (-1.4, 5.2, 3.6, 6)):
        shape(slide, x, y, d, d, fill=WHITE, alpha=a, kind=MSO_SHAPE.OVAL)

    logo = badge(slide, ML, 1.32, 0.82, "U", (WHITE, "E0E7FF"), font_size=30)
    logo.text_frame.paragraphs[0].runs[0].font.color.rgb = RGBColor.from_string(INDIGO_DEEP)

    tf = textbox(slide, ML + 1.06, 1.52, 6.0, 0.4)
    para(tf, "SAMAÉCOLE  ·  UNIKOL", size=12, color=VIOLET_LIGHT, bold=True, spacing=260)

    tf = textbox(slide, ML, 2.55, 10.4, 2.1)
    para(tf, "Le Système d'Information Éducatif\nNouvelle Génération",
         size=40, color=WHITE, bold=True, line_spacing=1.08)

    shape(slide, ML, 4.72, 1.5, 0.05, fill=VIOLET_LIGHT)
    tf = textbox(slide, ML, 4.98, 9.6, 0.5)
    para(tf, "Piloter votre établissement avec rigueur, modernité et simplicité.",
         size=16.5, color="DDD6FE", line_spacing=1.25)

    w, xs = cols(4, gap=0.24)
    facts = (("7", "modules métier intégrés"),
             ("23", "documents officiels PDF"),
             ("89", "services API sécurisés"),
             ("100 %", "en ligne, sans installation"))
    for (value, label), x in zip(facts, xs):
        sp = shape(slide, x, 5.85, w, 0.92, fill=WHITE, alpha=10,
                   kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.10)
        tf = sp.text_frame
        tf.margin_left = tf.margin_right = In(0.22)
        tf.margin_top = tf.margin_bottom = In(0.14)
        tf.vertical_anchor = MSO_ANCHOR.MIDDLE
        para(tf, value, size=17, color=WHITE, bold=True, line_spacing=1.0)
        para(tf, label, size=9, color="C7D2FE", line_spacing=1.1, space_before=2)

    tf = textbox(slide, ML, 6.98, CW, 0.3)
    para(tf, "Présentation commerciale et institutionnelle  —  Année scolaire 2026",
         size=9.5, color="C4B5FD", spacing=120)


def slide_02_constat(prs):
    slide = new_slide(prs)
    header(slide, "Le constat", "Ce que vit un chef d'établissement aujourd'hui",
           "Quatre points de rupture qui coûtent, chaque année, du temps, de l'argent et de la crédibilité.",
           accent=DANGER)
    w, xs = cols(4, gap=0.24)
    items = (
        ("Le calcul à la main",
         ["Moyennes, coefficients et rangs repris sur cahier ou tableur.",
          "Une erreur découverte après remise des bulletins ne se rattrape plus.",
          "Chaque trimestre, tout est à refaire."]),
        ("L'argent sans traçabilité",
         ["Reçus manuscrits, caisse jamais arrêtée formellement.",
          "Les impayés se découvrent en fin d'année, quand il est trop tard.",
          "Personne ne sait ce qui reste à recouvrer."]),
        ("Les bulletins en retard",
         ["Plusieurs nuits de saisie et de recopie par période.",
          "Les familles attendent, l'image de l'établissement en pâtit.",
          "La délibération se tient sans chiffres consolidés."]),
        ("Le pilotage à l'aveugle",
         ["Aucun indicateur consolidé : effectifs, assiduité, recettes.",
          "Les décisions se prennent au ressenti, pas sur des faits.",
          "Impossible de répondre vite à l'inspection ou aux promoteurs."]),
    )
    for (title, bullets), x in zip(items, xs):
        card(slide, x, CONTENT_TOP, w, 3.16, title, bullets, accent=DANGER,
             title_size=12.5, body_size=9.5, pad=0.26)
    band(slide, ML, 5.55, CW, 1.12,
         "Aucun de ces problèmes n'est un problème de personnes : vos équipes travaillent déjà "
         "beaucoup. Ce sont des problèmes d'outil — et un outil, cela se change.",
         label="Le vrai diagnostic", accent=DANGER, fill="FBEAE9", size=12.5)
    footer(slide, 2)


def slide_03_reponse(prs):
    slide = new_slide(prs)
    header(slide, "La réponse", "Une seule plateforme, du premier matricule au dernier bulletin",
           "100 % en ligne, accessible au navigateur depuis l'école ou de chez soi. Rien à installer, "
           "rien à sauvegarder à la main, aucune version à synchroniser entre les postes.")
    w, xs = cols(4, gap=0.24)
    tiles = (
        ("Fondations", "Années scolaires, classes, salles et capacités"),
        ("Pédagogie", "APC du primaire et notation coefficientée"),
        ("Inscriptions", "Matricules, dossiers élèves, réinscriptions"),
        ("Évaluation", "Notes, rangs, mentions, bulletins PDF"),
        ("Vie scolaire", "Appels, billets, discipline, convocations"),
        ("Finances", "Frais, échéanciers, caisse, recouvrement"),
        ("Pilotage", "Tableaux de bord et rapports de direction"),
        ("Socle & sécurité", "Isolation des données, rôles, journal d'audit"),
    )
    for i, (name, desc) in enumerate(tiles):
        row, col = divmod(i, 4)
        x, y = xs[col], CONTENT_TOP + row * 1.98
        last = (i == len(tiles) - 1)
        accent = VIOLET if last else BLEU_ROI
        shape(slide, x, y, w, 1.80, fill=WHITE if last else CARD_BG, line=accent if last else LINE,
              kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.08, line_w=1.25 if last else 1.0)
        badge(slide, x + 0.26, y + 0.26, 0.40, "✓" if last else str(i + 1),
              (accent, VIOLET_LIGHT if last else VIOLET), font_size=12)
        tf = textbox(slide, x + 0.26, y + 0.80, w - 0.52, 0.32)
        para(tf, name, size=12.5, color=INK, bold=True, line_spacing=1.05)
        tf = textbox(slide, x + 0.26, y + 1.14, w - 0.52, 0.52)
        para(tf, desc, size=9.5, color=BODY, line_spacing=1.18)
    band(slide, ML, 6.12, CW, 0.76,
         "Un seul dossier élève alimente l'inscription, les notes, l'assiduité et la comptabilité : "
         "l'information est saisie une fois, et une seule.", accent=VIOLET, size=11.5)
    footer(slide, 3)


def slide_04_fondations(prs):
    slide = new_slide(prs)
    header(slide, "Module 1 · Fondations",
           "Une année scolaire ouverte, pilotée, puis closée proprement",
           "Le paramétrage se fait une fois en début d'exercice ; tout le reste de l'année s'y rattache automatiquement.")
    w, xs = cols(3)
    card(slide, xs[0], CONTENT_TOP, w, 3.10, "Exercice annuel maîtrisé", [
        "Années scolaires et périodes (trimestres ou semestres) déclarées par l'établissement.",
        "Une seule année active à la fois : aucune saisie ne peut atterrir dans le mauvais exercice.",
        "Clôture de l'année sans perte : les archives restent consultables et imprimables.",
    ], num=1)
    card(slide, xs[1], CONTENT_TOP, w, 3.10, "Classes, cycles et effectifs", [
        "Classes rattachées à un cycle, avec capacité d'accueil et enseignant principal.",
        "Matières, coefficients et barèmes propres à chaque niveau.",
        "Classes passerelles accélérées (« CI-CP », « 6e-5e ») pour les parcours d'intégration — option "
        "désactivée par défaut.",
    ], num=2)
    card(slide, xs[2], CONTENT_TOP, w, 3.10, "Bâtiments et salles", [
        "Inventaire des bâtiments et des salles, avec capacité réelle.",
        "Affectation des classes aux salles, taux d'occupation du jour.",
        "Emploi du temps et pointage des enseignants adossés aux mêmes salles.",
    ], num=3)
    band(slide, ML, 5.50, CW, 1.10,
         "Chaque donnée est rattachée à une année scolaire précise. Les effectifs de 2024 ne polluent "
         "jamais les statistiques de 2026, et une réinscription n'écrase jamais l'historique de l'élève.",
         label="Ce que cela change", size=12)
    footer(slide, 4)


def slide_05_pedagogie(prs):
    slide = new_slide(prs)
    header(slide, "Module 2 · Dualité pédagogique",
           "L'APC du primaire et la notation coefficientée du secondaire",
           "Deux logiques d'évaluation radicalement différentes, servies par le même outil — sans compromis sur ni l'une ni l'autre.")
    w, xs = cols(2)
    card(slide, xs[0], CONTENT_TOP, w, 3.05, "Approche Par Compétences  ·  Primaire", [
        "Grille à deux niveaux : des domaines (« Langue & Communication ») qui portent des activités "
        "(« Ressources », « Compétences »).",
        "Barème libre par ligne — /10, /20, /60 — ramené automatiquement au barème du bulletin avant "
        "toute moyenne.",
        "Le bulletin imprime la grille ENTIÈRE, cases vides comprises, comme les modèles officiels.",
        "L'appréciation se décide sur le pourcentage de réussite, une seule échelle de mentions pour tous les barèmes.",
    ], num=1, body_size=10)
    card(slide, xs[1], CONTENT_TOP, w, 3.05, "Notation classique  ·  Collège et Lycée", [
        "Matières coefficientées, moyennes pondérées, moyenne générale.",
        "Rang de l'élève, moyenne de la classe, plus forte et plus faible moyenne.",
        "Mentions paramétrables par l'établissement, appréciations par matière et appréciation générale.",
        "Le tableau du bulletin ne liste que les matières réellement notées.",
    ], num=2, accent=VIOLET, body_size=10)
    band(slide, ML, 5.45, CW, 1.15,
         "Le Directeur configure lui-même la grille d'évaluation d'un niveau depuis l'écran Matières : "
         "aucun tableau de matières n'est figé dans le logiciel, et aucune intervention de l'éditeur "
         "n'est nécessaire pour faire évoluer un bulletin.",
         label="Autonomie totale de l'établissement", accent=VIOLET, size=12)
    footer(slide, 5)


def slide_06_inscriptions(prs):
    slide = new_slide(prs)
    header(slide, "Module 3 · Inscriptions & fichier élève",
           "Le dossier élève, de la première inscription jusqu'à l'archive",
           "Le guichet du secrétariat traite une inscription complète — dossier, frais, reçu — en une seule opération.")
    w, xs = cols(3)
    card(slide, xs[0], CONTENT_TOP, w, 3.05, "Matricule automatique", [
        "Généré au moment même de l'enregistrement, jamais à l'ouverture du formulaire.",
        "Ni doublon, ni numéro réservé puis perdu si l'agent abandonne la saisie.",
        "Format paramétrable par l'établissement, pour les élèves comme pour les enseignants.",
    ], num=1)
    card(slide, xs[1], CONTENT_TOP, w, 3.05, "Inscription et réinscription", [
        "Rattachement à l'année active et à la classe, avec contrôle de la capacité d'accueil.",
        "Application immédiate du barème de frais et de l'échéancier de la classe.",
        "Reçu d'inscription et attestation imprimés dans la foulée, remis à la famille.",
    ], num=2)
    card(slide, xs[2], CONTENT_TOP, w, 3.05, "Fichier et traçabilité", [
        "Profil complet : état civil, photo, tuteurs, contacts, historique de scolarité.",
        "Aucune suppression physique : une radiation reste réversible et reste traçable.",
        "Journal d'audit sur les opérations sensibles — qui a fait quoi, et quand.",
    ], num=3)
    band(slide, ML, 5.45, CW, 1.15,
         "Le reçu remis à la famille porte la mention réglementaire : « Il est demandé aux parents de "
         "garder minutieusement leur reçu après le paiement. » Le document est conforme au modèle "
         "officiel de l'établissement, pas à une interprétation.",
         label="Conformité documentaire", size=12)
    footer(slide, 6)


def slide_07_evaluation(prs):
    slide = new_slide(prs)
    header(slide, "Module 4 · Moteur d'évaluation",
           "Des notes saisies une fois, des bulletins prêts en un clic",
           "C'est le module qui fait basculer un établissement : il supprime purement et simplement les nuits de recopie.")
    w, xs = cols(3)
    card(slide, xs[0], CONTENT_TOP, w, 2.90, "Saisie centralisée", [
        "Feuille de notes par classe, matière et période — l'enseignant ne voit que ce qui le concerne.",
        "Import et export Excel pour les enseignants qui préfèrent leur tableur.",
        "Contrôle du barème dès la frappe : une note hors barème est refusée immédiatement.",
    ], num=1, body_size=10)
    card(slide, xs[1], CONTENT_TOP, w, 2.90, "Calculs automatiques", [
        "Moyennes pondérées par coefficient, moyenne générale, rang dans la classe.",
        "Moyenne de classe, extrêmes, mentions et appréciations déduites du barème.",
        "Zéro reprise manuelle : le même calcul sert à l'écran, au bulletin et à la délibération.",
    ], num=2, body_size=10)
    card(slide, xs[2], CONTENT_TOP, w, 2.90, "Bulletins et délibération", [
        "Bulletin PDF officiel, conforme au modèle de l'établissement.",
        "Génération en lot : toute une classe en un seul document, prêt à imprimer.",
        "PV de délibération et décisions (admis, redouble) archivés avec le reste.",
    ], num=3, body_size=10)
    w2, xs2 = cols(3)
    for (value, label, accent), x in zip(
        (("1 clic", "toute une classe de bulletins générée en un lot", BLEU_ROI),
         ("Excel", "import et export des notes, dans les deux sens", VIOLET),
         ("0", "écrasement silencieux : deux saisies simultanées sont détectées et signalées", SUCCESS)),
            xs2):
        kpi(slide, x, 5.24, w2, 1.42, value, label, accent=accent)
    footer(slide, 7)


def slide_08_vie_scolaire(prs):
    slide = new_slide(prs)
    header(slide, "Module 5 · Vie scolaire",
           "L'assiduité et la discipline tenues au jour le jour",
           "Le surveillant général dispose enfin d'un registre unique, imprimable, et opposable.")
    w, xs = cols(3)
    card(slide, xs[0], CONTENT_TOP, w, 3.05, "Assiduité et appels", [
        "Appel par cours ou par journée : présent, absent, retard, justifié.",
        "Taux d'assiduité du mois calculé automatiquement, par classe et par élève.",
        "Rapport de présences exporté en PDF pour le conseil ou l'inspection.",
    ], num=1)
    card(slide, xs[1], CONTENT_TOP, w, 3.05, "Billets et exeat", [
        "Billet d'entrée pour l'élève en retard, billet de sortie pour un départ anticipé.",
        "Exeat nominatif et daté, imprimé au moment même de la délivrance.",
        "Chaque billet reste au registre : il n'y a plus de sortie « non documentée ».",
    ], num=2)
    card(slide, xs[2], CONTENT_TOP, w, 3.05, "Discipline et convocations", [
        "Sanctions graduées, motif, décision et suites données.",
        "PV disciplinaire officiel et convocation de parent ou tuteur, remise en main propre.",
        "Historique de conduite consultable au dossier de l'élève.",
    ], num=3)
    band(slide, ML, 5.45, CW, 1.15,
         "Notification SMS et WhatsApp aux familles — retard, absence, échéance impayée, reçu : les envois "
         "passent par une file d'attente avec accusé de réception, de sorte qu'un message perdu se voit. "
         "Fonction incluse dans la formule Premium.",
         label="Communication sortante vers les parents", accent=VIOLET, size=12)
    footer(slide, 8)


def slide_09_finances(prs):
    slide = new_slide(prs)
    header(slide, "Module 6 · Comptabilité, caisse & recouvrement",
           "Chaque franc encaissé est tracé, chaque impayé est visible",
           "C'est ici que l'abonnement se rembourse : un établissement qui voit ses impayés les recouvre.")
    w, xs = cols(4, gap=0.24)
    blocks = (
        ("Barème et échéanciers", BLEU_ROI,
         ["Frais par classe et par cycle, définis par l'établissement.",
          "Échéanciers appliqués à une classe entière en une seule opération.",
          "Historique des barèmes conservé : un tarif révisé ne réécrit pas le passé."]),
        ("Encaissement au guichet", BLEU_ROI,
         ["Sessions de caisse ouvertes, arrêtées puis vérifiées.",
          "Reçu sécurisé imprimé et remis immédiatement à la famille.",
          "Journal de caisse du jour édité à la fermeture."]),
        ("Recouvrement des impayés", WARNING,
         ["État des impayés à jour, par classe et par famille.",
          "Avis d'échéance et relances, par SMS ou sur papier.",
          "Engagement financier signé par le tuteur, archivé au dossier."]),
        ("Trésorerie, paie et fiscalité", VIOLET,
         ["Encaissements et décaissements consolidés sur un même tableau.",
          "Fiches de paie, contrats et heures des enseignants.",
          "Déclarations TVA, VRS et IPRES préparées depuis les mêmes chiffres."]),
    )
    for i, ((title, accent, bullets), x) in enumerate(zip(blocks, xs)):
        card(slide, x, CONTENT_TOP, w, 3.16, title, bullets, num=i + 1, accent=accent,
             title_size=12, body_size=9.5, pad=0.26)
    band(slide, ML, 5.55, CW, 1.12,
         "Le service Finance ne modifie jamais lui-même un montant issu d'une inscription : toute "
         "correction passe par le Secrétariat ou la Direction, et reste historisée. La séparation "
         "des rôles est tenue par le logiciel, pas par la confiance.",
         label="Contrôle interne intégré", accent=WARNING, fill="FDF0E4", size=12)
    footer(slide, 9)


def slide_10_pilotage(prs):
    slide = new_slide(prs)
    header(slide, "Module 7 · Pilotage & statistiques",
           "Le tableau de bord que le Directeur ouvre en arrivant le matin",
           "Quatre chiffres en haut d'écran suffisent à savoir si la journée commence bien.")
    w, xs = cols(4, gap=0.24)
    tiles = (("Effectifs", "élèves inscrits sur l'année active, ventilés garçons / filles", BLEU_ROI),
             ("Enseignants", "enseignants actifs et heures assurées", VIOLET),
             ("Assiduité", "taux de présence du mois en cours, calculé sur les appels réels", SUCCESS),
             ("Occupation", "taux d'occupation des salles du jour et prochains cours", WARNING))
    for (value, label, accent), x in zip(tiles, xs):
        kpi(slide, x, CONTENT_TOP, w, 1.62, value, label, accent=accent, value_size=19)
    w2, xs2 = cols(3)
    card(slide, xs2[0], 4.00, w2, 2.36, "Rapports financiers consolidés", [
        "Recettes par cycle, par classe et par mode de paiement.",
        "Export .xlsx pour le comptable ou le conseil d'administration.",
    ], accent=BLEU_ROI, body_size=10, title_size=12.5)
    card(slide, xs2[1], 4.00, w2, 2.36, "Résultats et réussite", [
        "Moyennes et taux de réussite par classe et par matière.",
        "Comparaison entre périodes : la progression se voit, elle ne se suppose plus.",
    ], accent=VIOLET, body_size=10, title_size=12.5)
    card(slide, xs2[2], 4.00, w2, 2.36, "Abonnement et conformité", [
        "Formule en cours, échéance et jours restants affichés en clair.",
        "Journal d'audit consultable par la Direction à tout moment.",
    ], accent=SUCCESS, body_size=10, title_size=12.5)
    footer(slide, 10)


def slide_11_documents(prs):
    slide = new_slide(prs)
    header(slide, "Documents officiels",
           "23 documents officiels, générés, numérotés et archivés",
           "Tous produits en PDF, à l'en-tête de votre établissement, prêts à imprimer ou à remettre.")
    families = (
        ("Scolarité & fichiers", BLEU_ROI,
         ["Reçu d'inscription", "Reçu de paiement", "Attestation d'inscription",
          "Carte scolaire", "Export élèves"]),
        ("Évaluation & délibération", VIOLET,
         ["Bulletin de notes", "Bulletins par classe", "PV de délibération",
          "Rapport de présences"]),
        ("Vie scolaire & discipline", SUCCESS,
         ["Billet d'entrée", "Billet de sortie", "Exeat", "PV disciplinaire",
          "Convocation de parent"]),
        ("Finances & fiscalité", WARNING,
         ["Avis d'échéance", "Engagement financier", "Journal de caisse du jour",
          "Rapport d'arrêté de caisse", "Déclaration fiscale"]),
        ("Ressources humaines", INDIGO_DEEP,
         ["Bulletin de paie", "Attestation de travail", "Fiche d'heures enseignant",
          "Export enseignants"]),
    )
    cw, cxs = cols(5, gap=0.18)
    y = 2.05
    for label, accent, docs in families:
        tf = textbox(slide, ML, y, CW, 0.22)
        para(tf, "%s  ·  %d" % (label.upper(), len(docs)), size=8.5, color=accent,
             bold=True, spacing=150)
        for doc, x in zip(docs, cxs):
            chip(slide, x, y + 0.26, cw, 0.50, doc, accent=accent)
        y += 0.82

    tf = textbox(slide, ML, y + 0.10, CW, 0.5)
    para(tf, "Chaque document reprend le logo, la dénomination et les mentions légales de votre "
             "établissement — il n'y a rien à remettre en forme après coup. Le reçu porte la mention "
             "réglementaire demandée aux familles de conserver leur justificatif de paiement.",
         size=10.5, color=BODY, line_spacing=1.25)
    footer(slide, 11)


def slide_12_securite(prs):
    slide = new_slide(prs)
    header(slide, "Sécurité & multi-locataires",
           "Vos données sont à vous, et à personne d'autre",
           "La plateforme héberge plusieurs établissements. L'étanchéité entre eux n'est pas une promesse commerciale : elle est tenue par la base de données elle-même.")
    w, xs = cols(3)
    card(slide, xs[0], CONTENT_TOP, w, 3.18, "Isolation stricte des établissements", [
        "Chaque donnée porte l'identifiant de son école, sans exception.",
        "Double barrière : filtre applicatif ET sécurité au niveau des lignes côté PostgreSQL.",
        "Le compte technique de l'application n'est ni administrateur, ni propriétaire des tables : "
        "il ne peut pas contourner la barrière, même en cas de bogue.",
    ], num=1, body_size=10)
    card(slide, xs[1], CONTENT_TOP, w, 3.18, "Rôles et permissions", [
        "Six profils : Direction, Secrétariat, Finance, Enseignant, Surveillant, Administrateur.",
        "Chacun ne voit que son périmètre — un enseignant n'accède pas à la caisse.",
        "L'établissement du compte est porté par le jeton de connexion, jamais par un paramètre "
        "que l'utilisateur pourrait modifier.",
    ], num=2, accent=VIOLET, body_size=10)
    card(slide, xs[2], CONTENT_TOP, w, 3.18, "Traçabilité et maîtrise", [
        "Aucune suppression définitive de donnée métier : tout reste réversible.",
        "Journal d'audit horodaté sur les opérations sensibles.",
        "Remise à zéro des données d'essai réservée au Directeur, protégée par un mot de confirmation, "
        "et elle-même journalisée.",
    ], num=3, accent=SUCCESS, body_size=10)
    band(slide, ML, 5.58, CW, 1.08,
         "1 374 tests automatisés sont exécutés à chaque évolution du logiciel, dont une catégorie "
         "entièrement dédiée à l'étanchéité entre établissements, jouée contre une vraie base "
         "PostgreSQL. Une régression sur l'isolation des données ne peut pas atteindre la production sans être vue.",
         label="La preuve, pas la parole", accent=SUCCESS, fill="EAF7EE", size=12)
    footer(slide, 12)


def slide_13_journee(prs):
    slide = new_slide(prs)
    header(slide, "Démonstration", "Une journée type — le fil de la démonstration",
           "Ce que nous allons vous montrer en direct, dans l'ordre où votre établissement le vivra.")
    steps = (
        ("07h45", "Direction", "Le tableau de bord s'ouvre : effectifs du jour, assiduité d'hier, recettes de la semaine, échéance d'abonnement.", BLEU_ROI),
        ("08h30", "Secrétariat", "Un nouvel élève est inscrit : matricule attribué, frais appliqués, reçu et attestation imprimés devant la famille.", BLEU_ROI),
        ("09h15", "Surveillance", "L'appel est fait en classe. Un retard est enregistré, un billet d'entrée est édité, un SMS part vers le parent.", VIOLET),
        ("11h00", "Caisse", "Une échéance de scolarité est encaissée au guichet : reçu remis, session de caisse mise à jour à la seconde.", WARNING),
        ("15h00", "Enseignant", "Les notes de composition sont saisies, le barème est contrôlé à la frappe, moyennes et rangs se recalculent seuls.", VIOLET),
        ("17h30", "Clôture", "La caisse est arrêtée et justifiée ; les bulletins de la 6e A sont générés en un lot, prêts à imprimer.", SUCCESS),
    )
    y0, rh, gap = CONTENT_TOP, 0.72, 0.075
    shape(slide, ML + 0.325, y0 + 0.30, 0.022, (rh + gap) * (len(steps) - 1), fill=LINE)
    for i, (hour, actor, desc, accent) in enumerate(steps):
        y = y0 + i * (rh + gap)
        shape(slide, ML + 0.20, y + 0.22, 0.28, 0.28, fill=accent, kind=MSO_SHAPE.OVAL)
        shape(slide, ML + 0.265, y + 0.285, 0.15, 0.15, fill=WHITE, kind=MSO_SHAPE.OVAL)
        tf = textbox(slide, ML + 0.62, y, 0.95, rh, anchor=MSO_ANCHOR.MIDDLE)
        para(tf, hour, size=12.5, color=accent, bold=True, line_spacing=1.0)
        tf = textbox(slide, ML + 1.66, y, 1.72, rh, anchor=MSO_ANCHOR.MIDDLE)
        para(tf, actor, size=11.5, color=INK, bold=True, line_spacing=1.05)
        tf = textbox(slide, ML + 3.50, y, CW - 3.50, rh, anchor=MSO_ANCHOR.MIDDLE)
        para(tf, desc, size=10.5, color=BODY, line_spacing=1.2)
    footer(slide, 13)


def slide_14_offre(prs):
    slide = new_slide(prs)
    header(slide, "Accompagnement & abonnement",
           "Une mise en route en quelques jours, un abonnement lisible",
           "Trois formules, sans engagement de matériel ni licence à acheter. Le socle métier complet est inclus dès la première formule.")
    plans = (
        ("Primaire", "15 000", "150 000", BLEU_ROI, False,
         ["Élèves, classes, matières, inscriptions et réinscriptions",
          "Notes, bulletins et délibération",
          "Caisse, reçus et suivi des impayés",
          "Les 23 documents officiels"]),
        ("Standard", "25 000", "250 000", VIOLET, True,
         ["Tout le contenu de la formule Primaire",
          "Rapports financiers consolidés par cycle, classe et mode de paiement",
          "Exports comptables .xlsx",
          "Support prioritaire"]),
        ("Premium", "40 000", "400 000", INDIGO_DEEP, False,
         ["Tout le contenu de la formule Standard",
          "Notifications SMS et WhatsApp aux familles",
          "Groupe scolaire : plusieurs établissements pilotés d'un même compte",
          "Accompagnement dédié"]),
    )
    w, xs = cols(3)
    top, h = CONTENT_TOP - 0.04, 3.34
    for (name, monthly, yearly, accent, highlight, features), x in zip(plans, xs):
        shape(slide, x, top, w, h, fill=WHITE if highlight else CARD_BG,
              line=accent if highlight else LINE, kind=MSO_SHAPE.ROUNDED_RECTANGLE,
              radius=0.06, line_w=1.75 if highlight else 1.0)
        hdr = shape(slide, x, top, w, 0.52, kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.30,
                    grad=(accent, VIOLET) if highlight else None, fill=None if highlight else accent)
        tf = hdr.text_frame
        tf.margin_left = tf.margin_right = In(0.2)
        tf.margin_top = tf.margin_bottom = 0
        tf.vertical_anchor = MSO_ANCHOR.MIDDLE
        para(tf, name.upper(), size=11, color=WHITE, bold=True, align=PP_ALIGN.CENTER, spacing=200)

        tf = textbox(slide, x + 0.28, top + 0.76, w - 0.56, 0.5)
        p = para(tf, monthly, size=25, color=INK, bold=True, line_spacing=1.0)
        style_run(p.add_run(), 11, BODY).text = "  FCFA / mois"
        tf = textbox(slide, x + 0.28, top + 1.26, w - 0.56, 0.26)
        para(tf, "soit %s FCFA par an" % yearly, size=9.5, color=MUTED)
        shape(slide, x + 0.28, top + 1.60, w - 0.56, 0.012, fill=LINE)
        tf = textbox(slide, x + 0.28, top + 1.78, w - 0.56, h - 2.06)
        for i, b in enumerate(features):
            para(tf, b, size=9.5, color=BODY, line_spacing=1.2,
                 space_before=0 if i == 0 else 5, bullet="▪", bullet_color=accent)

    band(slide, ML, 5.58, CW, 0.98,
         "Reprise de vos données existantes, formation des équipes par rôle, support dédié et mises à jour "
         "incluses. Vous démarrez sur des données d'essai, puis remettez l'établissement à neuf en un clic le jour de la bascule.",
         label="Ce que l'abonnement comprend", size=11.5)
    tf = textbox(slide, ML, 6.64, CW, 0.22)
    para(tf, "Tarifs indicatifs, hors remise et hors offre de lancement — à confirmer avec votre conseiller.",
         size=8.5, color=MUTED, italic=True)
    footer(slide, 14)


def slide_15_conclusion(prs):
    slide = new_slide(prs)
    shape(slide, 0, 0, SW, SH, grad=(INDIGO_DEEP, VIOLET), grad_angle=35)
    for x, y, d, a in ((10.2, -2.0, 6.0, 9), (-1.8, 4.6, 4.4, 7)):
        shape(slide, x, y, d, d, fill=WHITE, alpha=a, kind=MSO_SHAPE.OVAL)

    tf = textbox(slide, ML, 1.30, CW, 0.34)
    para(tf, "CONCLUSION", size=11, color=VIOLET_LIGHT, bold=True, spacing=260)
    tf = textbox(slide, ML, 1.78, 10.6, 1.5)
    para(tf, "Passez au numérique dès cette année scolaire", size=36, color=WHITE, bold=True,
         line_spacing=1.08)
    shape(slide, ML, 3.42, 1.5, 0.05, fill=VIOLET_LIGHT)

    w, xs = cols(3)
    reasons = (
        ("Vous gagnez du temps", "Les bulletins, les reçus et les états de caisse cessent d'être un travail de nuit."),
        ("Vous gagnez de l'argent", "Les impayés deviennent visibles, donc recouvrables. L'abonnement se rembourse."),
        ("Vous gagnez en crédibilité", "Des documents nets, à l'heure, à votre en-tête — devant les familles comme devant l'inspection."),
    )
    for (title, desc), x in zip(reasons, xs):
        sp = shape(slide, x, 3.86, w, 1.62, fill=WHITE, alpha=11,
                   kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.08)
        tf = sp.text_frame
        tf.margin_left = tf.margin_right = In(0.26)
        tf.margin_top = In(0.24)
        tf.margin_bottom = In(0.20)
        para(tf, title, size=13, color=WHITE, bold=True, line_spacing=1.05)
        para(tf, desc, size=10, color="DDD6FE", line_spacing=1.22, space_before=6)

    sp = shape(slide, ML, 5.86, 5.4, 0.78, fill=WHITE, kind=MSO_SHAPE.ROUNDED_RECTANGLE, radius=0.22)
    tf = sp.text_frame
    tf.margin_left = tf.margin_right = In(0.3)
    tf.margin_top = tf.margin_bottom = 0
    tf.vertical_anchor = MSO_ANCHOR.MIDDLE
    para(tf, "Démonstration en direct — maintenant", size=14, color=INDIGO_DEEP, bold=True,
         align=PP_ALIGN.CENTER, line_spacing=1.0)

    tf = textbox(slide, ML + 5.9, 5.86, CW - 5.9, 0.78, anchor=MSO_ANCHOR.MIDDLE)
    para(tf, "SamaEcole · Unikol — Plateforme de gestion scolaire", size=11.5, color=WHITE, bold=True)
    para(tf, "Sénégal  ·  100 % en ligne  ·  Contact commercial : à compléter",
         size=9.5, color="C4B5FD", space_before=3)


# --------------------------------------------------------------------------------------
def build():
    prs = Presentation()
    prs.slide_width, prs.slide_height = In(SW), In(SH)
    for fn in (slide_01_cover, slide_02_constat, slide_03_reponse, slide_04_fondations,
               slide_05_pedagogie, slide_06_inscriptions, slide_07_evaluation,
               slide_08_vie_scolaire, slide_09_finances, slide_10_pilotage,
               slide_11_documents, slide_12_securite, slide_13_journee,
               slide_14_offre, slide_15_conclusion):
        fn(prs)
    prs.save(OUTPUT)
    print("OK  %s  —  %d slides" % (OUTPUT, len(prs.slides._sldIdLst)))


if __name__ == "__main__":
    build()
