using Xunit;

// AuthApiFactory configure l'API de test par variables d'environnement (seul moyen d'être lu avant
// l'enregistrement des services en minimal hosting — voir le commentaire d'AuthApiFactory). Or ces
// variables sont globales au processus : deux classes de tests tournant en parallèle se voleraient
// leur chaîne de connexion et taperaient dans le conteneur l'une de l'autre.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
