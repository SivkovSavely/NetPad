use std::{collections::HashMap, fs, path::PathBuf, sync::{atomic::{AtomicU64, Ordering}, Mutex}};
use serde::{Deserialize, Serialize};
use tauri::{AppHandle, State, WebviewWindow};
use tauri_plugin_dialog::DialogExt;

static NEXT_HANDLE: AtomicU64 = AtomicU64::new(1);
const MAX_HANDLES: usize = 2000;

#[derive(Default)]
pub struct TextFileState(Mutex<HashMap<String, (String, String)>>);

#[derive(Serialize, Deserialize)]
pub struct TextFile { handle: String, path: String, text: String }
#[derive(Serialize, Deserialize)]
pub struct TextFileReference { handle: String, path: String }

fn next_handle() -> String {
    format!("{:x}-{:x}", std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap().as_nanos(), NEXT_HANDLE.fetch_add(1, Ordering::Relaxed))
}
fn issue(state: &TextFileState, owner: &str, path: PathBuf) -> TextFileReference {
    let path = path.to_string_lossy().into_owned();
    let handle = next_handle();
    let mut handles = state.0.lock().unwrap();
    handles.insert(handle.clone(), (owner.to_owned(), path.clone()));
    if handles.len() > MAX_HANDLES {
        if let Some(oldest) = handles.keys().next().cloned() {
            if oldest != handle { handles.remove(&oldest); }
        }
    }
    TextFileReference { handle, path }
}
fn resolve(state: &TextFileState, owner: &str, handle: &str) -> Result<PathBuf, String> {
    state.0.lock().unwrap().get(handle).filter(|(label, _)| label == owner)
        .map(|(_, path)| PathBuf::from(path)).ok_or_else(|| "Invalid text-file handle".into())
}
fn read(path: &PathBuf) -> Result<String, String> {
    let bytes = fs::read(path).map_err(|e| e.to_string())?;
    if bytes.contains(&0) { return Err("Text file contains NUL bytes".into()); }
    String::from_utf8(bytes).map_err(|e| e.to_string())
}
fn picker(app: &AppHandle, options: serde_json::Value, save: bool) -> Vec<PathBuf> {
    let mut builder = app.dialog().file();
    if let Some(title) = options["title"].as_str() { builder = builder.set_title(title); }
    if let Some(default_path) = options["defaultPath"].as_str() {
        let path = PathBuf::from(default_path);
        if save {
            if let Some(file_name) = path.file_name().and_then(|name| name.to_str()) {
                builder = builder.set_file_name(file_name);
            }
            if let Some(parent) = path.parent() {
                builder = builder.set_directory(parent);
            }
        } else {
            builder = builder.set_directory(path);
        }
    }
    if let Some(filters) = options["filters"].as_array() {
        for filter in filters {
            if let (Some(name), Some(exts)) = (filter["name"].as_str(), filter["extensions"].as_array()) {
                let exts: Vec<&str> = exts.iter().filter_map(|v| v.as_str()).collect();
                builder = builder.add_filter(name, &exts);
            }
        }
    }
    let selected = if save { builder.blocking_save_file().into_iter().collect() } else { builder.blocking_pick_files().unwrap_or_default() };
    selected.into_iter().filter_map(|file| file.into_path().ok()).collect()
}

#[tauri::command]
pub fn open_text_files(app: AppHandle, window: WebviewWindow, state: State<TextFileState>, options: serde_json::Value) -> Result<Option<Vec<TextFile>>, String> {
    let paths = picker(&app, options, false);
    if paths.is_empty() { return Ok(None); }
    let opened = paths.into_iter().map(|path| {
        let text = read(&path)?;
        Ok((path, text))
    }).collect::<Result<Vec<_>, String>>()?;
    opened.into_iter().map(|(path, text)| {
        let reference = issue(&state, window.label(), path);
        Ok(TextFile { handle: reference.handle, path: reference.path, text })
    }).collect::<Result<Vec<_>, String>>().map(Some)
}

#[tauri::command]
pub fn reopen_text_file(window: WebviewWindow, state: State<TextFileState>, path: String) -> Result<TextFile, String> {
    let path = PathBuf::from(path);
    if !path.is_absolute() { return Err("Invalid text-file path".into()); }
    let text = read(&path)?;
    let reference = issue(&state, window.label(), path);
    Ok(TextFile { handle: reference.handle, path: reference.path, text })
}

#[tauri::command]
pub fn read_text_file(window: WebviewWindow, state: State<TextFileState>, handle: String) -> Result<String, String> { read(&resolve(&state, window.label(), &handle)?) }
#[tauri::command]
pub fn save_text_file(window: WebviewWindow, state: State<TextFileState>, handle: String, text: String) -> Result<(), String> { fs::write(resolve(&state, window.label(), &handle)?, text).map_err(|e| e.to_string()) }
#[tauri::command]
pub fn save_text_file_as(app: AppHandle, window: WebviewWindow, state: State<TextFileState>, text: String, options: serde_json::Value) -> Result<Option<TextFileReference>, String> {
    let Some(path) = picker(&app, options, true).into_iter().next() else { return Ok(None); };
    fs::write(&path, text).map_err(|e| e.to_string())?;
    Ok(Some(issue(&state, window.label(), path)))
}
