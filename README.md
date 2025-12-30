
# Summer Project 2025/26

This is a Unity project created for personal experimentation and development.
This repository contains the latest version of the Summer Project.


## Overview

- **Unity Version:**
- **Description:** This project is a sandbox for testing Unity features and workflows. It includes scenes, assets, and scripts for experimentation.
- **Repository Type:** Dual remote (GitLab for storage and LFS, GitHub for visibility)

## Features

- Sample Unity scenes
- Custom scripts for testing gameplay mechanics
- Support for large asset files via Git LFS

## Getting Started

### Prerequisites

- [Unity Hub](https://unity.com/download) installed
- Compatible Unity version (as specified above)
- Git installed
- Git LFS installed for large files

### Clone the repository

```
bash
git clone https://gitlab.com/JeffreyUoB/summer-project-on-gitlab.git
git lfs install
git lfs pull
```


## Pushing Changes to GitHub and GitLab

To push your current branch to **both GitHub and GitLab** at the same time:

1. Make sure your remotes are set up:

```
bash
# GitHub
git remote add origin <github-url>

# GitLab
git remote add gitlab <gitlab-url>
```

You only need to do this once per clone.
Use this command to push the current branch to both repositories:
```
git push --set-upstream origin HEAD && git push --set-upstream gitlab HEAD
```

in the future:
```
git add .
git commit -m "Your message here"
git push origin HEAD && git push gitlab HEAD
```


HEAD ensures the current branch is pushed.--set-upstream makes sure your branch is tracked by both remotes for future pushes.
On future updates, you can just run the same command to push changes


Notes

If you ever need to force push (replace history completely), you can use:
```
git push --force origin HEAD && git push --force gitlab HEAD
```

Make sure large files in Unity (like .unity scenes) are handled with Git LFS if you want to avoid GitHub size warnings: Git LFS
