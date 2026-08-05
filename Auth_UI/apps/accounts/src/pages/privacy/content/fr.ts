import type { PrivacyPolicyContent } from "./types"

export const fr: PrivacyPolicyContent = {
  title: "Politique de confidentialité",
  effectiveDate: "En vigueur au 28 juillet 2026",
  versionLabel: "Version",
  unfilledWarning:
    "Brouillon — les coordonnées du responsable du traitement ne sont pas encore renseignées. Cette page ne doit pas être publiée avant que tous les champs soient complétés.",
  contactDpoLabel: "Délégué à la protection des données",
  contactVerbisLabel: "Numéro d'enregistrement VERBİS",
  contactKepLabel: "Courriel enregistré (KEP)",
  intro: [
    `Cette politique explique quelles données personnelles le service de comptes de {{legalName}} collecte, pourquoi nous les collectons, combien de temps nous les conservons et les droits dont vous disposez — y compris la manière de supprimer votre compte et tout ce qui s'y rattache.`,
    "Elle vaut également information au titre de l'article 10 de la loi turque n° 6698 sur la protection des données personnelles (KVKK) — notre cadre de conformité principal — et est rédigée pour satisfaire au Règlement général sur la protection des données de l'UE/EEE (RGPD) ainsi qu'au California Consumer Privacy Act tel que modifié (CCPA/CPRA). Les contrôles décrits dans cette politique sont offerts à chaque utilisateur, quel que soit son lieu de résidence.",
  ],
  sections: [
    {
      heading: "Les données que nous collectons",
      paragraphs: [
        "Nous ne collectons que ce dont le service de comptes a besoin pour fonctionner. Nous ne demandons jamais davantage, et les champs facultatifs sont clairement facultatifs.",
      ],
      bullets: [
        "Compte et profil : adresse e-mail, prénom et nom, nom d'affichage facultatif, numéro de téléphone facultatif (stocké chiffré), photo de profil facultative, langue préférée, fuseau horaire et thème.",
        "Identifiants et paramètres de sécurité : votre mot de passe (stocké uniquement sous forme d'empreinte Argon2id à sens unique — nous ne pouvons pas le lire), le secret d'authentification à deux facteurs et les codes de récupération facultatifs (stockés chiffrés), et l'historique des changements de mot de passe (empreintes uniquement, pour empêcher la réutilisation).",
        "Connexion avec Google ou Apple : votre identifiant, votre e-mail et votre nom tels que partagés par le fournisseur. Pour Apple, un jeton de révocation est stocké chiffré dans le seul but de révoquer l'autorisation de connexion Apple lorsque vous supprimez votre compte.",
        "Journaux de sécurité et d'utilisation : tentatives de connexion (heure, adresse IP, identifiant du navigateur, résultat), sessions et jetons actifs, et un journal d'audit des actions liées au compte.",
        "Communications : une trace des e-mails de service que nous vous envoyons (codes de vérification, avis de sécurité, confirmations de suppression). Le contenu des messages porteurs de codes à usage unique ou de liens de connexion est retiré de cette trace dès la remise de l'e-mail — même nos administrateurs ne peuvent pas le lire.",
      ],
    },
    {
      heading: "Pourquoi nous les traitons (bases légales)",
      paragraphs: [
        "Vos données sont collectées par voie électronique, via les formulaires et les parcours de connexion du service. Chaque finalité repose sur une base légale au titre de l'article 5 de la KVKK et de son équivalent à l'article 6 du RGPD :",
      ],
      bullets: [
        "Fournir votre compte — authentification, sessions, profil, appartenance aux organisations (nécessité contractuelle : KVKK art. 5/2-c ; RGPD art. 6(1)(b)).",
        "Sécuriser les comptes — journaux de tentatives de connexion, limitation de débit, révocation de sessions, journal d'audit, prévention de la fraude (intérêts légitimes : KVKK art. 5/2-f ; RGPD art. 6(1)(f)).",
        "Respecter nos obligations légales — conserver la preuve minimale qu'une demande de suppression a été honorée (KVKK art. 5/2-ç ; RGPD art. 6(1)(c)).",
        "Données facultatives — le numéro de téléphone et la photo de profil ne sont traités que parce que vous avez choisi de les fournir, et vous pouvez les retirer à tout moment (consentement explicite : KVKK art. 5/1 ; RGPD art. 6(1)(a)).",
      ],
    },
    {
      heading: "Ce que nous ne faisons pas",
      paragraphs: [
        "Nous ne vendons pas vos données personnelles et ne les partageons pas à des fins de publicité comportementale intercontexte — au sens du CCPA, aucune « vente » ni aucun « partage » n'a eu lieu au cours des 12 derniers mois et aucun n'est prévu. Le service de comptes n'embarque aucun traceur publicitaire ni outil d'analyse tiers, et vos données ne servent à aucune décision automatisée produisant des effets juridiques ou d'importance similaire.",
      ],
    },
    {
      heading: "Cookies et stockage local",
      paragraphs: [
        "Le service de comptes n'utilise que ce que la connexion exige strictement : un cookie de session essentiel qui vous maintient connecté sur nos propres pages, et le stockage local du navigateur qui contient le jeton de renouvellement de votre session ainsi que vos préférences de langue et de thème. Il n'y a aucun cookie d'analyse, de publicité ou de suivi intersite.",
      ],
    },
    {
      heading: "Avec qui nous partageons les données",
      paragraphs: [
        "Nous ne partageons les données personnelles qu'avec les sous-traitants qui font fonctionner le service, et seulement dans la mesure nécessaire :",
      ],
      bullets: [
        "Google (connexion avec Google) et Apple (connexion avec Apple) — uniquement lorsque vous choisissez de vous connecter avec eux ; l'échange est régi par leurs propres politiques de confidentialité.",
        `Notre fournisseur d'envoi d'e-mails, {{emailProvider}}, pour envoyer les e-mails de service décrits ci-dessus.`,
        `Notre hébergeur, {{hostingProvider}}, qui stocke les données du service.`,
        "Les autorités publiques, si et seulement si une demande légale valable nous y contraint.",
      ],
    },
    {
      heading: "Transferts internationaux",
      paragraphs: [
        `Le service est hébergé en/au {{hostingCountry}}. Lorsque des données personnelles quittent la Türkiye, le transfert s'effectue au titre de l'article 9 de la KVKK : une décision d'adéquation du Conseil de protection des données personnelles lorsqu'elle existe, sinon les garanties appropriées prévues par cet article (comme le contrat type du Conseil, notifié à l'Autorité comme requis). Pour les données quittant l'EEE ou le Royaume-Uni, nous nous appuyons en outre sur des décisions d'adéquation ou sur les clauses contractuelles types de la Commission européenne. Les mesures de sécurité ci-dessous s'appliquent dans tous les cas.`,
      ],
    },
    {
      heading: "Comment nous les protégeons",
      paragraphs: ["La sécurité est en couches et s'applique à chaque compte :"],
      bullets: [
        "Les mots de passe sont hachés avec Argon2id ; les codes de vérification ne sont stockés que sous forme d'empreintes ; les liens de réinitialisation uniquement sous forme de condensats HMAC.",
        "Votre numéro de téléphone, votre secret à deux facteurs et le jeton de révocation Apple sont chiffrés en AES-256-GCM sous une clé propre à votre compte.",
        "Tout le trafic est chiffré en transit (TLS). La connexion est protégée par la limitation de débit et le verrouillage de compte ; les sessions peuvent être révoquées à tout moment et le sont toutes instantanément lorsque vous demandez la suppression.",
        "Les actions du compte sont consignées dans un journal d'audit afin de détecter et d'examiner toute activité suspecte.",
      ],
    },
  ],
  retention: {
    heading: "Durées de conservation",
    intro:
      "Nous ne conservons pas les données personnelles plus longtemps que la finalité ne l'exige. Ces durées sont appliquées automatiquement par le système, sans intervention manuelle :",
    columns: ["Données", "Durée", "Ce qui se passe ensuite"],
    rows: [
      {
        category: "Données de compte et de profil",
        retention:
          "Jusqu'à la suppression de votre compte (+ {{graceDays}} jours de rétractation)",
        detail:
          "Détruites définitivement par le processus de suppression par étapes décrit ci-dessous.",
      },
      {
        category: "Empreintes de mots de passe et secrets à deux facteurs",
        retention: "Jusqu'à la suppression du compte",
        detail:
          "Stockés uniquement hachés ou chiffrés ; détruits avec le compte.",
      },
      {
        category: "Sessions et jetons",
        retention: "Jusqu'à expiration ou déconnexion",
        detail:
          "Tous révoqués immédiatement lorsque la suppression est demandée.",
      },
      {
        category: "Journaux de tentatives de connexion (adresse IP incluse)",
        retention: "{{loginAttemptRetentionDays}} jours",
        detail:
          "Purgés automatiquement ; dépersonnalisés immédiatement à la suppression du compte.",
      },
      {
        category: "Journal d'audit de sécurité",
        retention: "Dépersonnalisé à la suppression du compte",
        detail:
          "Tous les champs personnels sont retirés ; le fait qu'une suppression a été exécutée (type d'événement et horodatage uniquement) est conservé au moins 3 ans comme preuve légale.",
      },
      {
        category: "Trace des e-mails de service envoyés",
        retention: "{{outboxRetentionDays}} jours",
        detail: "Purgée automatiquement.",
      },
      {
        category: "Codes de vérification de suppression",
        retention: "{{otpValidityMinutes}} minutes (validité du code)",
        detail:
          "Stockés uniquement sous forme d'empreintes ; les entrées expirées sont supprimées par le nettoyage quotidien.",
      },
      {
        category: "Trace de suppression (identifiant haché)",
        retention: "{{identifierReservationDays}} jours",
        detail:
          "Condensat HMAC à sens unique et à clé de l'e-mail supprimé, conservé pour que personne — vous y compris — ne puisse réenregistrer cette adresse tant que la réservation dure. Une adresse ne peut pas être lue à partir d'un condensat, mais nous conservons la clé qui permet de tester une adresse connue : cet enregistrement est donc pseudonymisé, et non anonyme. Il est supprimé à l'expiration du délai et l'adresse redevient disponible.",
      },
      {
        category: "Sauvegardes",
        retention: "6 mois au plus",
        detail:
          "La rotation des sauvegardes est appliquée par la configuration de rétention de la plateforme d'hébergement. Indépendamment de cela, la suppression du compte détruit la clé de chiffrement de vos champs chiffrés, rendant ces données définitivement illisibles, même dans les sauvegardes existantes.",
      },
    ],
  },
  deletion: {
    heading: "Supprimer votre compte",
    paragraphs: [
      "Vous pouvez demander à tout moment la suppression définitive de votre compte et de vos données personnelles, sans contacter personne. La demande est enregistrée immédiatement et exécutée sous {{graceDays}} jours :",
    ],
    bullets: [
      "Votre compte est désactivé sur-le-champ et déconnecté de tous les appareils ; toutes les sessions, tous les jetons et toutes les autorisations de connexion sont révoqués immédiatement.",
      "Pendant {{graceDays}} jours, vous pouvez changer d'avis — vous reconnecter restaure le compte et annule la suppression. Vous recevez un e-mail de confirmation à chaque étape.",
      "Passé ce délai de {{graceDays}} jours, la suppression s'exécute automatiquement et est irréversible : les données de profil sont effacées, les journaux de sécurité sont dépersonnalisés, les clés de chiffrement propres au compte sont détruites (destruction cryptographique, couvrant les sauvegardes), et — si vous utilisiez la connexion avec Apple — Apple est invité à révoquer l'autorisation de connexion.",
      "Votre adresse e-mail et votre nom d'utilisateur ne sont jamais recyclés : seuls des condensats à sens unique subsistent, si bien que personne d'autre ne pourra jamais les enregistrer.",
    ],
    button: "Supprimer mon compte",
    signedInHint:
      "Connecté ? Vous pouvez aussi le faire depuis l'onglet Compte de votre profil (Zone de danger).",
  },
  rights: [
    {
      heading: "Vos droits en Türkiye (KVKK)",
      paragraphs: [
        "En vertu de l'article 11 de la loi n° 6698, en tant que personne concernée vous avez le droit de savoir si vos données sont traitées, de demander des informations sur ce traitement et sa finalité, de connaître les tiers, en Türkiye ou à l'étranger, auxquels elles sont transférées, de demander la rectification de données incomplètes ou inexactes, d'en demander la suppression ou la destruction au titre de l'article 7, de demander que rectifications et suppressions soient notifiées aux destinataires, de vous opposer à un résultat produit exclusivement par une analyse automatisée, et de demander réparation du préjudice causé par un traitement illicite.",
        "Les demandes s'adressent au responsable du traitement via les coordonnées ci-dessous — par écrit, via notre adresse de courriel enregistré (KEP) lorsqu'elle est fournie, ou depuis une adresse e-mail que vous nous avez préalablement déclarée — conformément au communiqué sur les modalités de demande. Nous répondons sous 30 jours au plus tard, gratuitement. En cas de rejet ou d'absence de réponse, vous pouvez saisir le Conseil de protection des données personnelles.",
      ],
    },
    {
      heading: "Vos droits dans l'EEE et au Royaume-Uni (RGPD)",
      paragraphs: [
        "Vous avez le droit d'accéder à vos données, de les faire rectifier, de les faire effacer, de restreindre le traitement ou de vous y opposer, d'en recevoir une copie portable, et de retirer votre consentement à tout moment lorsque le traitement repose sur le consentement. Vous pouvez exercer la plupart de ces droits directement dans l'application (édition du profil, suppression du compte) ; pour le reste, contactez-nous aux coordonnées ci-dessous. Vous avez également le droit d'introduire une réclamation auprès de votre autorité de contrôle nationale.",
      ],
    },
    {
      heading: "Vos droits en Californie (CCPA/CPRA)",
      paragraphs: [
        "Vous avez le droit de savoir quelles informations personnelles nous collectons et d'y accéder, de les corriger, de les supprimer, et de ne subir aucune discrimination pour avoir exercé ces droits. Comme nous ne vendons ni ne partageons d'informations personnelles et n'utilisons les informations personnelles sensibles que pour fournir le service, il n'y a rien à refuser. Nous honorons les demandes vérifiées sous 45 jours. Vous pouvez passer par un agent autorisé pour soumettre une demande en votre nom.",
      ],
    },
    {
      heading: "Tous les autres utilisateurs",
      paragraphs: [
        "Les mêmes contrôles — accès, rectification, suppression et le parcours de suppression dans l'application décrit ci-dessus — sont offerts à chaque utilisateur, où que vous viviez.",
      ],
    },
  ],
  closing: [
    {
      heading: "Enfants",
      paragraphs: [
        "Le service ne s'adresse pas aux enfants et ne peut pas être utilisé par des personnes de moins de 16 ans. Nous ne collectons pas sciemment de données d'enfants ; si vous pensez qu'un enfant a créé un compte, contactez-nous et nous le supprimerons.",
      ],
    },
    {
      heading: "Modifications de cette politique",
      paragraphs: [
        "Chaque révision de cette politique porte un numéro de version (année.mois, affiché en haut de page). En cas de changement substantiel, nous vous préviendrons par e-mail ou par un avis dans l'application avant son entrée en vigueur. Les demandes de suppression sont toujours honorées selon les conditions en vigueur au moment de la demande.",
      ],
    },
    {
      heading: "Contact et réclamations",
      paragraphs: [
        `Responsable du traitement : {{legalName}}, {{address}}. Contact confidentialité : {{privacyEmail}}. Nous répondons aux demandes d'exercice de droits sous 30 jours (KVKK/RGPD) et 45 jours (CCPA). Si la réponse ne vous satisfait pas, vous pouvez saisir l'autorité de contrôle compétente : en Türkiye l'Autorité de protection des données personnelles, dans l'EEE/au Royaume-Uni votre autorité nationale de protection des données, en Californie la California Privacy Protection Agency ou le procureur général.`,
      ],
    },
  ],
}
