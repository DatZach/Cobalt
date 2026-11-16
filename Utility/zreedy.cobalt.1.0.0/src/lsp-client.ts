import * as path from 'path';
import * as vscode from 'vscode';

import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;

export async function activate(context: vscode.ExtensionContext) {
    outputChannel = outputChannel || vscode.window.createOutputChannel("Cobalt Language Server");

    const config = vscode.workspace.getConfiguration("cobalt");
    const serverDirectory = config.get<string>("toolchainDirectory");
    if (serverDirectory == null) {
        return vscode.window.showErrorMessage("Cobalt Toolchain Directory not specified")
            .then(_ => undefined);
    }

    const serverPath = path.join(serverDirectory, 'Compiler.exe');
    const serverOptions: ServerOptions = {
        command: serverPath,
        transport: TransportKind.stdio,
        args: ["--language-server"],
        options: {
            cwd: serverDirectory
        }
    };

    const clientOptions: LanguageClientOptions = {
        outputChannel: outputChannel,
        documentSelector: [{ scheme: 'file', language: 'cob' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/.clientrc'),
            configurationSection: 'cobalt'
        }
    };
    
    client = new LanguageClient(
        'cobServer',
        'Cobalt Language Server',
        serverOptions,
        clientOptions
    );

    client.start();

    context.subscriptions.push(client);
}

export function deactivate(): Thenable<void> | undefined {
    if (!client)
		return undefined;

	return client.stop();
}
