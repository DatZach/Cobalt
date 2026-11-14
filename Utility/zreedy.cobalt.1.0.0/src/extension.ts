import * as vscode from 'vscode';
import * as lspClient from './lsp-client';
import * as dapClient from './dap-client';

export function activate(context: vscode.ExtensionContext) {
	const lspResult = lspClient.activate(context);
	const dapResult = dapClient.activate(context);
	return { ...lspResult, ...dapResult };
}

export function deactivate(): Thenable<void> | undefined {
	lspClient.deactivate();
	return undefined;
}
