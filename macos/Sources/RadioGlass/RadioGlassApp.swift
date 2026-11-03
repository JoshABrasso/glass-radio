import AppKit
import SwiftUI

@main
struct GlassRadioApp: App {
    @StateObject private var viewModel = AppViewModel()
    @Environment(\.openWindow) private var openWindow

    var body: some Scene {
        WindowGroup("Glass Radio") {
            ContentView()
                .environmentObject(viewModel)
                .environmentObject(viewModel.player)
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
                if #available(macOS 14.0, *) {
                    SettingsLink {
                        Text("Settings…")
                    }
                } else {
                    Button("Settings…") {
                        NSApp.sendAction(Selector(("showSettingsWindow:")), to: nil, from: nil)
                    }
                    .keyboardShortcut(",", modifiers: .command)
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

            CommandMenu("View") {
                Button(viewModel.isMiniGamePresented ? "Close Mini Game" : "Open Mini Game") {
                    viewModel.isMiniGamePresented.toggle()
                }
                .keyboardShortcut("g", modifiers: [.command, .option])
            }

            CommandMenu("Playback") {
                Button("Play/Pause") {
                    viewModel.player.togglePlayback()
                }
                .keyboardShortcut(.space, modifiers: [])

                Divider()

                Button(viewModel.player.eq.isEnabled ? "Disable Equalizer" : "Enable Equalizer") {
                    viewModel.player.eq.isEnabled.toggle()
                }
                .keyboardShortcut("e", modifiers: [.command, .shift])

                Button("Equalizer Settings…") {
                    openWindow(id: "equalizer")
                }
                .keyboardShortcut("e", modifiers: [.command])
            }
        }
        
        Settings {
            PreferencesView()
                .environmentObject(viewModel)
                .environmentObject(viewModel.player)
        }

        Window("Equalizer", id: "equalizer") {
            EQView(eq: viewModel.player.eq)
                .padding(0)
        }
        .windowResizability(.contentSize)
        .defaultPosition(.topTrailing)
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
