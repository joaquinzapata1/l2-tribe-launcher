namespace L2InterludeUpdater;

internal enum LauncherLanguage
{
    Spanish,
    English,
    Portuguese
}

internal sealed record LauncherStrings(
    string Version,
    string Checking,
    string NoRelease,
    string Ready,
    string Install,
    string Update,
    string Play,
    string Cancel,
    string ChooseFolder,
    string Repair,
    string ManualInstall,
    string FolderDialog,
    string ClientAvailable,
    string SearchCanceled,
    string PreparingRepair,
    string PreparingClient,
    string DownloadingManifest,
    string OperationCanceled,
    string SelectManifest,
    string VerifyingManifest,
    string ClientReady,
    string NoBackup,
    string Backup,
    string InstalledFiles,
    string DownloadedPackages,
    string DeletedFiles,
    string ExistingFolder,
    string ExistingFolderTitle,
    string Canceling,
    string ClientNotInstalled,
    string ClientUnavailable,
    string ShortcutError,
    string OperationInProgress,
    string OperationInProgressTitle,
    string GenericErrorTitle);

internal static class LauncherLocalization
{
    public static LauncherStrings For(LauncherLanguage language) => language switch
    {
        LauncherLanguage.English => English,
        LauncherLanguage.Portuguese => Portuguese,
        _ => Spanish
    };

    public static LauncherLanguage Parse(string? value) => value?.ToUpperInvariant() switch
    {
        "EN" => LauncherLanguage.English,
        "PT" => LauncherLanguage.Portuguese,
        _ => LauncherLanguage.Spanish
    };

    public static string Code(LauncherLanguage language) => language switch
    {
        LauncherLanguage.English => "EN",
        LauncherLanguage.Portuguese => "PT",
        _ => "ES"
    };

    private static readonly LauncherStrings Spanish = new(
        "VERSION", "Buscando version...", "Sin release", "Listo.",
        "INSTALAR", "ACTUALIZAR", "JUGAR", "CANCELAR",
        "Elegir carpeta", "Reparar cliente", "Instalacion manual",
        "Elegi donde instalar el cliente", "Cliente listo.", "Busqueda cancelada.",
        "Preparando reparacion...", "Preparando cliente...", "Descargando manifiesto... {0}%",
        "Operacion cancelada sin cambios incompletos.", "Elegi un manifiesto de cliente",
        "Verificando manifiesto local...", "Cliente {0} listo.",
        "No habia archivos anteriores para respaldar.", "Backup: {0}",
        "Archivos instalados: {0}", "Paquetes descargados: {0}", "Borrados: {0}",
        "La carpeta no esta vacia. Se respaldaran los archivos reemplazados. Continuar?",
        "Carpeta existente", "Cancelando...", "El cliente todavia no esta instalado.",
        "Cliente no disponible", "No se pudo crear el acceso directo: {0}",
        "Cancela la operacion actual antes de cerrar.", "Operacion en curso",
        "No se pudo completar la operacion");

    private static readonly LauncherStrings English = new(
        "VERSION", "Checking version...", "No release", "Ready.",
        "INSTALL", "UPDATE", "PLAY", "CANCEL",
        "Choose folder", "Repair client", "Manual install",
        "Choose where to install the client", "Client ready.", "Update check canceled.",
        "Preparing repair...", "Preparing client...", "Downloading manifest... {0}%",
        "Operation canceled without incomplete changes.", "Choose a client manifest",
        "Verifying local manifest...", "Client {0} is ready.",
        "There were no previous files to back up.", "Backup: {0}",
        "Installed files: {0}", "Downloaded packages: {0}", "Deleted: {0}",
        "The folder is not empty. Replaced files will be backed up. Continue?",
        "Existing folder", "Canceling...", "The client is not installed yet.",
        "Client unavailable", "Could not create the launcher shortcut: {0}",
        "Cancel the current operation before closing.", "Operation in progress",
        "The operation could not be completed");

    private static readonly LauncherStrings Portuguese = new(
        "VERSAO", "Buscando versao...", "Sem release", "Pronto.",
        "INSTALAR", "ATUALIZAR", "JOGAR", "CANCELAR",
        "Escolher pasta", "Reparar cliente", "Instalacao manual",
        "Escolha onde instalar o cliente", "Cliente pronto.", "Busca cancelada.",
        "Preparando reparo...", "Preparando cliente...", "Baixando manifesto... {0}%",
        "Operacao cancelada sem alteracoes incompletas.", "Escolha um manifesto do cliente",
        "Verificando manifesto local...", "Cliente {0} pronto.",
        "Nao havia arquivos anteriores para backup.", "Backup: {0}",
        "Arquivos instalados: {0}", "Pacotes baixados: {0}", "Excluidos: {0}",
        "A pasta nao esta vazia. Os arquivos substituidos serao salvos. Continuar?",
        "Pasta existente", "Cancelando...", "O cliente ainda nao esta instalado.",
        "Cliente indisponivel", "Nao foi possivel criar o atalho: {0}",
        "Cancele a operacao atual antes de fechar.", "Operacao em andamento",
        "Nao foi possivel concluir a operacao");
}
