# Publishing this documentation

Source pages are UTF-8 Markdown in `docs/wiki`. Links use `.md` for repository browsing. The export helper can remove that suffix for GitHub Wiki routes.

Commands below assume a source checkout. In the launcher bundle, replace `docs/` with `documentation/`.

## GitHub Wiki

1. Choose the repository and enable Wiki if needed. Create its first page on GitHub.
2. Clone the separate wiki repository using GitHub's displayed URL.
3. Export into a new local folder:

   ```powershell
   ./docs/Export-Wiki.ps1 -Destination '.\wiki-export' -LinkStyle Wiki
   ```

4. Review the output, then copy the exported `.md` files into your wiki checkout. Keep `Home.md`, `_Sidebar.md` and `_Footer.md` at its root.
5. Commit, review the diff and push when ready to publish.

The helper only copies local documents. It does not create a repository, change a remote, commit or push. It refuses to overwrite an existing destination directory.

GitHub's [wiki editing guide](https://docs.github.com/en/communities/documenting-your-project-with-wikis/adding-or-editing-wiki-pages) explains initialization/cloning. `_Sidebar.md` and `_Footer.md` follow its [sidebar/footer convention](https://docs.github.com/en/communities/documenting-your-project-with-wikis/creating-a-footer-or-sidebar-for-your-wiki).

## Repository documentation

Keep the source folder and open `Home.md`, or export with `-LinkStyle Repository`. No documentation framework is required. GitHub Pages hosting is a separate choice; it is not configured here.

Before public release, supply real repository/download URLs, confirm package/game versions and review project/asset licenses. Only the starter template is declared MIT-licensed here. No broader licensing or public upload is implied.
