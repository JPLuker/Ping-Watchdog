using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Velopack;
using Velopack.Sources;
using PingWatchdog.Shared;

namespace PingWatchdog;

// Folders and site organization, and the Organization window.
public sealed partial class MainForm
{
    internal static string NormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return string.Join(
            "/",
            path
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => !string.IsNullOrWhiteSpace(segment)));
    }

    private static string FolderParent(string path)
    {
        path = NormalizeFolderPath(path);
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    private static string FolderName(string path)
    {
        path = NormalizeFolderPath(path);
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static string CombineFolderPath(string parent, string name)
    {
        parent = NormalizeFolderPath(parent);
        name = name.Trim();

        return string.IsNullOrWhiteSpace(parent)
            ? NormalizeFolderPath(name)
            : NormalizeFolderPath($"{parent}/{name}");
    }

    private void EnsureFolderHierarchy(string? path)
    {
        path = NormalizeFolderPath(path);

        if (string.IsNullOrWhiteSpace(path))
            return;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string current = string.Empty;

        foreach (var part in parts)
        {
            current = CombineFolderPath(current, part);

            if (!_siteFolders.Any(folder =>
                folder.Equals(current, StringComparison.OrdinalIgnoreCase)))
            {
                _siteFolders.Add(current);
            }
        }
    }

    private string FormatSiteDisplayPath(SiteDefinition site)
    {
        string folder = NormalizeFolderPath(site.FolderPath);

        return string.IsNullOrWhiteSpace(folder)
            ? site.Name
            : $"{folder.Replace("/", " › ")} › {site.Name}";
    }

    private OrganizationSnapshot BuildOrganizationSnapshot()
    {
        return new OrganizationSnapshot(
            _siteFolders
                .Select(NormalizeFolderPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            _sites
                .OrderBy(site => site.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
                .Select(site => new OrganizationSiteSnapshot(
                    site.Name,
                    NormalizeFolderPath(site.FolderPath),
                    site.Hosts.Count))
                .ToList());
    }

    private string? ValidateFolderName(string name)
    {
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return "Enter a folder name.";

        if (name.Contains('/') || name.Contains('\\'))
            return "Folder names cannot contain / or \\.";

        if (name is "." or "..")
            return "Choose a different folder name.";

        return null;
    }

    private string? AddOrganizationFolder(string parentPath, string name)
    {
        string? error = ValidateFolderName(name);
        if (error is not null)
            return error;

        parentPath = NormalizeFolderPath(parentPath);
        string path = CombineFolderPath(parentPath, name);

        if (_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "A folder with that name already exists here.";
        }

        EnsureFolderHierarchy(path);
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private string? RenameOrganizationFolder(string path, string newName)
    {
        path = NormalizeFolderPath(path);

        if (!_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "That folder no longer exists.";
        }

        string? error = ValidateFolderName(newName);
        if (error is not null)
            return error;

        string parent = FolderParent(path);
        string replacement = CombineFolderPath(parent, newName);

        if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) &&
            _siteFolders.Any(folder =>
                folder.Equals(replacement, StringComparison.OrdinalIgnoreCase)))
        {
            return "A folder with that name already exists here.";
        }

        RewriteFolderPrefix(path, replacement);
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private string? DeleteOrganizationFolder(string path)
    {
        path = NormalizeFolderPath(path);

        if (!_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "That folder no longer exists.";
        }

        string parent = FolderParent(path);
        RewriteFolderPrefix(path, parent);
        _siteFolders.RemoveAll(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase));

        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private string? MoveOrganizationFolder(string path, string newParent)
    {
        path = NormalizeFolderPath(path);
        newParent = NormalizeFolderPath(newParent);

        if (!_siteFolders.Any(folder =>
            folder.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            return "That folder no longer exists.";
        }

        if (!string.IsNullOrWhiteSpace(newParent) &&
            !_siteFolders.Any(folder =>
                folder.Equals(newParent, StringComparison.OrdinalIgnoreCase)))
        {
            return "The destination folder no longer exists.";
        }

        if (newParent.Equals(path, StringComparison.OrdinalIgnoreCase) ||
            newParent.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))
        {
            return "A folder cannot be moved inside itself.";
        }

        string replacement = CombineFolderPath(newParent, FolderName(path));

        if (!replacement.Equals(path, StringComparison.OrdinalIgnoreCase) &&
            _siteFolders.Any(folder =>
                folder.Equals(replacement, StringComparison.OrdinalIgnoreCase)))
        {
            return "A folder with that name already exists in the destination.";
        }

        RewriteFolderPrefix(path, replacement);
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private void RewriteFolderPrefix(string oldPath, string newPath)
    {
        oldPath = NormalizeFolderPath(oldPath);
        newPath = NormalizeFolderPath(newPath);

        var rewritten = new List<string>();

        foreach (var folder in _siteFolders)
        {
            string normalized = NormalizeFolderPath(folder);

            if (normalized.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(newPath))
                    rewritten.Add(newPath);
            }
            else if (normalized.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = normalized[(oldPath.Length + 1)..];
                rewritten.Add(CombineFolderPath(newPath, suffix));
            }
            else
            {
                rewritten.Add(normalized);
            }
        }

        _siteFolders.Clear();

        foreach (var folder in rewritten
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            EnsureFolderHierarchy(folder);
        }

        foreach (var site in _sites)
        {
            string siteFolder = NormalizeFolderPath(site.FolderPath);

            if (siteFolder.Equals(oldPath, StringComparison.OrdinalIgnoreCase))
            {
                site.FolderPath = newPath;
            }
            else if (siteFolder.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                string suffix = siteFolder[(oldPath.Length + 1)..];
                site.FolderPath = CombineFolderPath(newPath, suffix);
            }
        }
    }

    private string? AddOrganizationSite(string name, string folderPath)
    {
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return "Enter a site name.";

        if (name.Equals(AllSitesLabel, StringComparison.OrdinalIgnoreCase))
            return "\"All Sites\" is reserved.";

        if (_sites.Any(site =>
            site.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return "A site with that name already exists.";
        }

        folderPath = NormalizeFolderPath(folderPath);

        if (!string.IsNullOrWhiteSpace(folderPath))
            EnsureFolderHierarchy(folderPath);

        PersistCurrentEditor();

        _sites.Add(new SiteDefinition
        {
            Name = name,
            FolderPath = folderPath
        });

        _selectedSiteName = name;
        SaveSites();
        ReconcileMonitoringWithConfig();
        RefreshSiteList(name);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
        return null;
    }

    private string? RenameOrganizationSite(string siteName, string newName)
    {
        return WallboardRenameSite(siteName, newName);
    }

    private string? DeleteOrganizationSite(string siteName)
    {
        return WallboardDeleteSite(siteName);
    }

    private string? MoveOrganizationSite(string siteName, string folderPath)
    {
        var site = FindSite(siteName);

        if (site is null)
            return "That site no longer exists.";

        folderPath = NormalizeFolderPath(folderPath);

        if (!string.IsNullOrWhiteSpace(folderPath))
            EnsureFolderHierarchy(folderPath);

        site.FolderPath = folderPath;
        SaveSites();
        RefreshSiteList(_selectedSiteName);
        return null;
    }

    private void SelectOrganizationSite(string siteName)
    {
        if (FindSite(siteName) is null)
            return;

        PersistCurrentEditor();
        _selectedSiteName = siteName;
        RefreshSiteList(siteName);
        LoadHostEditor();
        RefreshGrid();
        RebuildCommandView();
        UpdateActionState();
    }

    private void OpenOrganization(IWin32Window? owner = null)
    {
        if (_organizationForm is not null && !_organizationForm.IsDisposed)
        {
            _organizationForm.Show();
            _organizationForm.Activate();
            _organizationForm.RefreshNow();
            return;
        }

        _organizationForm = new OrganizationForm(
            BuildOrganizationSnapshot,
            new OrganizationActions(
                AddOrganizationFolder,
                RenameOrganizationFolder,
                DeleteOrganizationFolder,
                MoveOrganizationFolder,
                AddOrganizationSite,
                RenameOrganizationSite,
                DeleteOrganizationSite,
                MoveOrganizationSite,
                SelectOrganizationSite));

        _organizationForm.FormClosed += (_, _) => _organizationForm = null;
        _organizationForm.Show(owner ?? this);
    }
}
