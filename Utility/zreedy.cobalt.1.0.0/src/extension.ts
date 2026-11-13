import * as vscode from 'vscode';
import * as lspClient from './lsp-client';

export function activate(context: vscode.ExtensionContext) {
	const lspResult = lspClient.activate(context);
	return lspResult;
}

export function deactivate(): Thenable<void> | undefined {
	lspClient.deactivate();
	return undefined;
}
