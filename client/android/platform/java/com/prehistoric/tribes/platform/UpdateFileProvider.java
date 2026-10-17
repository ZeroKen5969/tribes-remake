package com.prehistoric.tribes.platform;

import androidx.core.content.FileProvider;

/**
 * Shares downloaded update APKs with the system installer. A subclass of its own, because the
 * Godot library already declares androidx.core.content.FileProvider (authority
 * "&lt;application id&gt;.fileprovider"), and the manifest merger would fold two declarations
 * with the same class name into one.
 */
public class UpdateFileProvider extends FileProvider {
}
