# Setting up Rafter Cat's Vogue Studio

Same steps as your other Rafter Cat plugins.

1. On GitHub (as The-Kinship), make a new **public** repo called `vogue-studio`. Leave it empty.
2. Click **uploading an existing file**, drag in everything from this zip, commit.
3. If the `.github` folder was skipped: **Add file > Create new file**, name it `.github/workflows/build.yml`,
   paste in that file's contents (open it from the unzipped folder in Notepad), commit.
4. **Settings > Actions > General > Workflow permissions**: **Read and write permissions**, Save.
5. **Actions** tab: wait for "Build plugin" to go green (or press **Run workflow**).
6. In game: `/xlsettings` > **Experimental** > add
   `https://raw.githubusercontent.com/The-Kinship/vogue-studio/main/repo.json`, press +, Save.
7. `/xlplugins`, search **Vogue Studio**, Install.

Try it: enter GPose, type `/vogue`. Circles should sit on your joints, with names.
Click a few circles and names in the list. Move the camera: the circles should follow.
