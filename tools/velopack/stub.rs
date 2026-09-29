// DockPad stable launcher. Derived from Velopack's MIT-licensed Windows stub.
// Keep URL requests in memory while current/ is being replaced; never log arguments.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]
use std::{os::windows::process::CommandExt, process::{Command, ExitCode}, time::{Duration, SystemTime}};
use windows::Win32::UI::WindowsAndMessaging::AllowSetForegroundWindow;

fn main() -> ExitCode {
    let me = match std::env::current_exe() { Ok(p) => p, Err(_) => return ExitCode::FAILURE };
    let root = me.parent().unwrap();
    let marker = root.join(".dockpad-update");
    let args: Vec<_> = std::env::args_os().skip(1).collect();
    // A dead updater must not leave the launcher waiting forever.
    for _ in 0..3000 {
        let active = marker.metadata().and_then(|m| m.modified()).ok()
            .and_then(|t| SystemTime::now().duration_since(t).ok())
            .map(|age| age < Duration::from_secs(300)).unwrap_or(false);
        if !active && root.join("current").join(me.file_name().unwrap()).exists() { break; }
        std::thread::sleep(Duration::from_millis(100));
    }
    let mut command = Command::new(root.join("current").join(me.file_name().unwrap()));
    command.args(&args).current_dir(root).creation_flags(0x08000000);
    match command.spawn() {
        Ok(mut child) => {
            let _ = unsafe { AllowSetForegroundWindow(child.id()) };
            // MCP clients own the lifetime of their child and its inherited stdio.
            if args.iter().any(|a| a == "--mcp") {
                return match child.wait() { Ok(s) if s.success() => ExitCode::SUCCESS, _ => ExitCode::FAILURE };
            }
            ExitCode::SUCCESS
        }
        Err(_) => ExitCode::FAILURE,
    }
}
