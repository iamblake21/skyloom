namespace CML.Content
{
    public static class CatalogSchema
    {
        public const int CurrentVersion = 1;
        // bootstrap-11: Fibra vegetale, Bastone e la ricetta del Piccone che
        // li usa entrambi. La revisione distingue due contenuti diversi,
        // quindi va mossa a ogni voce aggiunta o due cataloghi differenti
        // dichiarerebbero lo stesso identificativo.
        public const string BootstrapContentRevision = "bootstrap-11";
    }
}
