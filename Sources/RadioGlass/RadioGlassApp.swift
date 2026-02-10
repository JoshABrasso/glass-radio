import AppKit
import SwiftUI

@main
struct RadioGlassApp: App {
    @StateObject private var viewModel = AppViewModel()

    var body: some Scene {
        WindowGroup("Glass Radio") {
            ContentView()
                .environmentObject(viewModel)
                .frame(minWidth: 1180, minHeight: 760)
        }
        .windowStyle(.automatic)
        .windowToolbarStyle(.automatic)
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("About Glass Radio") {
                    showAboutPanel()
                }
            }

            CommandGroup(replacing: .appSettings) {
                SettingsLink {
                    Text("Preferences…")
                }
            }

            CommandGroup(after: .appInfo) {
                Button("Check for Updates…") {
                    showInfoAlert(
                        title: "No Updates Available",
                        message: "Glass Radio is up to date."
                    )
                }
            }

            CommandMenu("Help") {
                Button("Glass Radio Help") {
                    showInfoAlert(
                        title: "Help",
                        message: "Help content will be available in a future update."
                    )
                }
            }

            CommandMenu("Playback") {
                Button("Play/Pause") {
                    viewModel.player.togglePlayback()
                }
                .keyboardShortcut(.space, modifiers: [])
            }
        }
        
        Settings {
            PreferencesView()
        }
    }

    private func showAboutPanel() {
        let options: [NSApplication.AboutPanelOptionKey: Any] = [
            .applicationName: "Glass Radio",
            .applicationVersion: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.0",
            .version: Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String ?? "1",
            .credits: NSAttributedString(string: "Global radio, curated and refined.")
        ]
        NSApp.orderFrontStandardAboutPanel(options: options)
        NSApp.activate(ignoringOtherApps: true)
    }

    private func showInfoAlert(title: String, message: String) {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = message
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }
}
