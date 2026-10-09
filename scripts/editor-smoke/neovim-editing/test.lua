local config = vim.json.decode(table.concat(vim.fn.readfile(assert(vim.env.SHARPTS_EDITOR_SMOKE_CONFIG)), '\n'))
local result = { mode = config.mode, editor = tostring(vim.version()), cases = {}, sources = {}, clients = {}, observations = {} }
local clients, diagnostics, diagnostic_counts = {}, {}, {}
local phase = 'start'
vim.o.swapfile = false
vim.lsp.log.set_level('debug')
result.log = vim.lsp.log.get_filename()
local default_diagnostics = vim.lsp.handlers['textDocument/publishDiagnostics']
local function present(value) return value ~= nil and value ~= vim.NIL end
local function uri_key(uri)
  local path = vim.fs.normalize(vim.uri_to_fname(uri)):gsub('\\', '/')
  return vim.fn.has('win32') == 1 and path:lower() or path
end
local function text(buf) return table.concat(vim.api.nvim_buf_get_lines(buf, 0, -1, false), '\n') .. '\n' end
local function scenario(name, fn)
  phase = name
  fn()
  table.insert(result.cases, name)
end
local function wait_until(fn, message)
  assert(vim.wait(15000, fn, 20), message)
end
local function open(name)
  vim.cmd.edit(vim.fn.fnameescape(config.files[name].path))
  local buf = vim.api.nvim_get_current_buf()
  vim.bo[buf].filetype = name:match('%.tsx$') and 'typescriptreact' or 'typescript'
  return buf
end
local function set_source(buf, source)
  local lines = vim.split(source:gsub('\n$', ''), '\n', { plain = true })
  vim.api.nvim_buf_set_lines(buf, 0, -1, false, lines)
end
local function offset(buf, marker)
  local source = text(buf)
  local comment = '/*' .. marker .. '*/'
  local at = assert(source:find(comment, 1, true), 'Missing marker ' .. marker)
  local after = at - 1 + #comment
  while source:sub(after + 1, after + 1):match('%s') do after = after + 1 end
  return after
end
local function position(buf, at)
  local source = text(buf)
  local prefix = source:sub(1, at)
  local _, line = prefix:gsub('\n', '\n')
  local line_start = (prefix:match('.*()\n') or 0)
  return { line = line, character = vim.str_utfindex(source:sub(line_start + 1):match('^[^\n]*'), 'utf-16', at - line_start) }
end
local function params(buf, at)
  return { textDocument = { uri = vim.uri_from_bufnr(buf) }, position = position(buf, at) }
end
local function request(client, method, buf, at, extra)
  local arguments = vim.tbl_extend('force', params(buf, at), extra or {})
  local response = assert(client:request_sync(method, arguments, 15000, buf), method .. ' timed out')
  assert(not response.err, method .. ': ' .. vim.inspect(response.err))
  return response.result
end
local function diagnostic_count(client, buf)
  return (diagnostic_counts[client.id] or {})[uri_key(vim.uri_from_bufnr(buf))] or 0
end
local function wait_diagnostics(client, buf, before, predicate, version)
  local uri = uri_key(vim.uri_from_bufnr(buf))
  wait_until(function()
    local values = (diagnostics[client.id] or {})[uri]
    return diagnostic_count(client, buf) > before and values
      and (version == nil or values.version == version) and predicate(values.diagnostics)
  end, 'Fresh expected diagnostics missing for ' .. client.name .. ': ' .. vim.inspect(diagnostics[client.id]))
end
local function attach(client, buf) assert(vim.lsp.buf_attach_client(buf, client.id)) end
local function start(name, buf, versioned)
  local capabilities = vim.lsp.protocol.make_client_capabilities()
  capabilities.general.positionEncodings = { 'utf-16' }
  if versioned then capabilities.workspace.workspaceEdit.documentChanges = true end
  local command, init_options
  if name == 'typescript' then
    command = { config.node, config.typescriptCli, '--stdio' }
    init_options = { disableAutomaticTypingAcquisition = true, hostInfo = 'SharpTS Neovim editing smoke',
      tsserver = { path = config.tsserver, useSyntaxServer = 'never', logDirectory = config.workspace .. '/tsserver-logs', logVerbosity = 'normal' } }
  else
    command = { config.dotnet, config.server, '--language-features', config.mode == 'full' and 'full' or 'interop-only',
      '--diagnostics', config.mode == 'full' and 'all' or 'sharpts-only' }
  end
  vim.api.nvim_set_current_buf(buf)
  local id = assert(vim.lsp.start({ name = name, cmd = command, root_dir = config.workspace, capabilities = capabilities,
    init_options = init_options, flags = { debounce_text_changes = 20 },
    handlers = {
      ['textDocument/publishDiagnostics'] = function(err, notification, ctx, options)
        diagnostics[ctx.client_id] = diagnostics[ctx.client_id] or {}
        diagnostic_counts[ctx.client_id] = diagnostic_counts[ctx.client_id] or {}
        local key = uri_key(notification.uri)
        diagnostics[ctx.client_id][key] = notification
        diagnostic_counts[ctx.client_id][key] = (diagnostic_counts[ctx.client_id][key] or 0) + 1
        if default_diagnostics then default_diagnostics(err, notification, ctx, options) end
      end,
      ['$/typescriptVersion'] = function(_, notification) result.typescriptBackend = notification end,
    },
  }, { reuse_client = function() return false end }))
  clients[id] = true
  local client = assert(vim.lsp.get_client_by_id(id))
  wait_until(function() return client.initialized end, name .. ' initialize timed out')
  table.insert(result.clients, { id = id, name = name, command = command, capabilities = capabilities,
    serverCapabilities = client.server_capabilities, offsetEncoding = client.offset_encoding })
  assert(client.offset_encoding == 'utf-16', 'Exact UTF-16 negotiation required')
  return client
end
local function stop(client)
  client:stop()
  wait_until(function() return client:is_stopped() end, 'Server failed graceful stop: ' .. client.name)
end
local function list(value)
  if not present(value) then return {} end
  if value.uri or value.targetUri then return { value } end
  return value.items or value
end
local function hover(client, buf, marker, spelling)
  local value = request(client, 'textDocument/hover', buf, offset(buf, marker) + 1)
  assert(present(value), 'Expected hover from ' .. client.name)
  local encoded = vim.json.encode(value)
  assert(encoded:find(spelling, 1, true), 'Hover missing ' .. spelling .. ': ' .. encoded)
  return value
end
local function expect_definition(client, buf, marker, dependency_text)
  local values = list(request(client, 'textDocument/definition', buf, offset(buf, marker) + 1))
  assert(#values == 1, vim.inspect(values))
  local expected_at = assert(dependency_text:find('/*depDeclaration*/', 1, true)) - 1 + #'/*depDeclaration*/'
  local prefix = dependency_text:sub(1, expected_at)
  local _, line = prefix:gsub('\n', '\n')
  local line_start = prefix:match('.*()\n') or 0
  local character = vim.str_utfindex(dependency_text:sub(line_start + 1):match('^[^\n]*'), 'utf-16', expected_at - line_start)
  local target_uri = values[1].uri or values[1].targetUri
  local target_range = values[1].range or values[1].targetSelectionRange
  local function canonical(path)
    local normalized = vim.fs.normalize(path):gsub('\\', '/')
    return vim.fn.has('win32') == 1 and normalized:lower() or normalized
  end
  assert(canonical(vim.uri_to_fname(target_uri)) == canonical(config.files['dep.ts'].path), vim.inspect(values))
  assert(target_range.start.line == line and target_range.start.character == character, vim.inspect(values))
  return values
end
local function native_hover(buf, at, expected)
  vim.api.nvim_set_current_buf(buf)
  local source = text(buf)
  local prefix = source:sub(1, at)
  local _, line = prefix:gsub('\n', '\n')
  local line_start = prefix:match('.*()\n') or 0
  vim.api.nvim_win_set_cursor(0, { line + 1, at - line_start })
  vim.lsp.buf.hover({ silent = true })
  local rendered
  wait_until(function()
    for _, win in ipairs(vim.api.nvim_list_wins()) do
      if vim.api.nvim_win_get_config(win).relative ~= '' then
        local lines = vim.api.nvim_buf_get_lines(vim.api.nvim_win_get_buf(win), 0, -1, false)
        local contents = table.concat(lines, '\n')
        if contents:find(expected, 1, true) then rendered = contents; return true end
      end
    end
    return false
  end, 'Native hover UI missing ' .. expected)
  for _, win in ipairs(vim.api.nvim_list_wins()) do
    if vim.api.nvim_win_get_config(win).relative ~= '' then vim.api.nvim_win_close(win, true) end
  end
  return rendered
end
local function save_and_verify(client, buf, name, marker, expected_hover)
  vim.api.nvim_set_current_buf(buf)
  set_source(buf, config.files[name].source .. '  ')
  assert(vim.bo[buf].modified)
  hover(client, buf, marker, expected_hover)
  local before = diagnostic_count(client, buf)
  vim.cmd.write()
  local file = assert(io.open(config.files[name].path, 'rb'))
  local actual = file:read('*a'); file:close()
  assert(actual == config.files[name].formatted, 'Format-on-save differs from pinned Prettier: ' .. name)
  if client.name ~= 'typescript' then
    wait_diagnostics(client, buf, before, function() return true end, vim.lsp.util.buf_versions[buf])
  end
  hover(client, buf, marker, expected_hover)
end

local function run()
  dofile(config.recipe)
  local main = open('main.ts')
  local sharp = start('sharpts', main)
  assert(not sharp:supports_method('textDocument/formatting', main))
  if config.mode == 'full' then
    scenario('full-native-hover-definition-references', function()
      hover(sharp, main, 'member', 'number')
      expect_definition(sharp, main, 'member', config.files['dep.ts'].source)
      local refs = list(request(sharp, 'textDocument/references', main, offset(main, 'member') + 1,
        { context = { includeDeclaration = true } }))
      assert(#refs == 3, vim.inspect(refs))
      local expected = position(main, offset(main, 'member'))
      local main_ref
      for _, location in ipairs(refs) do
        if uri_key(location.uri) == uri_key(vim.uri_from_bufnr(main)) then main_ref = location end
      end
      assert(main_ref and main_ref.range.start.line == expected.line
        and main_ref.range.start.character == expected.character
        and main_ref.range['end'].character == expected.character + #'field', vim.inspect(refs))
      result.observations.memberReferences = refs
      result.nativeOrdinaryHover = native_hover(main, offset(main, 'member') + 1, 'number')
    end)
    scenario('unfinished-receiver-completion-applied-by-native-client', function()
      local header = "import { Model, pair } from './dep';\nconst item = new Model();\n"
      set_source(main, header .. 'item.')
      local completions = list(request(sharp, 'textDocument/completion', main, #text(main) - 1))
      local chosen
      for _, item in ipairs(completions) do if item.label == 'field' then chosen = item end end
      assert(chosen and chosen.textEdit, vim.inspect(completions))
      result.observations.appliedCompletion = chosen
      vim.lsp.util.apply_text_edits({ chosen.textEdit }, main, sharp.offset_encoding)
      assert(text(main):find('item.field', 1, true))
      local at = assert(text(main):find('item.field', 1, true)) - 1 + #'item.'
      local fresh = request(sharp, 'textDocument/hover', main, at + 1)
      assert(present(fresh) and vim.json.encode(fresh):find('number', 1, true))
      set_source(main, config.files['main.ts'].source)
    end)
    scenario('unfinished-call-signatures-without-a-false-winner', function()
      set_source(main, "import { pair } from './dep';\npair(1, ")
      local help = request(sharp, 'textDocument/signatureHelp', main, #text(main) - 1,
        { context = { triggerKind = 2, triggerCharacter = ',', isRetrigger = true } })
      assert(present(help) and #help.signatures == 1, vim.inspect(help))
      assert(help.signatures[1].label:find('count: number', 1, true), vim.inspect(help))
      assert(help.activeParameter == 1 and not present(help.activeSignature), vim.inspect(help))
      result.observations.unfinishedSignature = help
      set_source(main, config.files['main.ts'].source)
    end)
    scenario('native-lexical-workspace-edit-and-fresh-hover', function()
      local prepared = request(sharp, 'textDocument/prepareRename', main, offset(main, 'lexicalUse') + 1)
      assert(present(prepared))
      local edit = request(sharp, 'textDocument/rename', main, offset(main, 'lexicalUse') + 1, { newName = 'renamedLocal' })
      assert(present(edit) and edit.changes)
      vim.lsp.util.apply_workspace_edit(edit, sharp.offset_encoding)
      assert(text(main):find('/*lexicalUse*/renamedLocal', 1, true))
      hover(sharp, main, 'lexicalUse', 'renamedLocal: 1')
      set_source(main, config.files['main.ts'].source)
    end)
    scenario('dirty-dependency-diagnostics-target-ranges-and-close', function()
      local dependency = open('dep.ts'); attach(sharp, dependency)
      local before = diagnostic_count(sharp, main)
      local dirty = '// dirty dependency shifted\n' .. config.files['dep.ts'].source:gsub('field', 'changedField')
      set_source(dependency, dirty)
      request(sharp, 'textDocument/hover', dependency, offset(dependency, 'depDeclaration') + 1)
      request(sharp, 'textDocument/hover', main, offset(main, 'member') + 1)
      wait_diagnostics(sharp, main, before, function(values) return #values > 0 end)
      set_source(main, config.files['main.ts'].source:gsub('field', 'changedField'))
      hover(sharp, main, 'member', 'number')
      result.observations.dirtyDependencyDefinition = expect_definition(sharp, main, 'member', dirty)
      vim.api.nvim_buf_delete(dependency, { force = true })
      before = diagnostic_count(sharp, main)
      set_source(main, config.files['main.ts'].source)
      hover(sharp, main, 'member', 'number')
      wait_diagnostics(sharp, main, before, function(values) return #values == 0 end)
      expect_definition(sharp, main, 'member', config.files['dep.ts'].source)
    end)
    scenario('ts-and-tsx-one-save-formatter-with-fresh-language-requests', function()
      save_and_verify(sharp, main, 'main.ts', 'member', 'number')
      local view = open('view.tsx'); attach(sharp, view)
      save_and_verify(sharp, view, 'view.tsx', 'tsxUse', 'number')
    end)
    local private = open('private.ts'); attach(sharp, private)
    scenario('native-default-capabilities-refuse-private-edits', function()
      assert(sharp.config.capabilities.workspace.workspaceEdit.documentChanges ~= true)
      assert(not present(request(sharp, 'textDocument/prepareRename', private, offset(private, 'privateUse') + 1)))
      assert(not present(request(sharp, 'textDocument/rename', private, offset(private, 'privateUse') + 1, { newName = 'new' })))
      result.defaultPrivateRename = 'refused: documentChanges is absent from native capabilities'
    end)
    stop(sharp)
    sharp = start('sharpts-versioned-private', private, true)
    scenario('explicit-versioned-capability-applies-private-domain-natively', function()
      local prepared = request(sharp, 'textDocument/prepareRename', private, offset(private, 'privateUse') + 1)
      assert(present(prepared) and prepared.placeholder == '#old', vim.inspect(prepared))
      local at = position(private, offset(private, 'privateUse'))
      assert(prepared.range.start.line == at.line and prepared.range.start.character == at.character)
      assert(prepared.range['end'].character - prepared.range.start.character == 4)
      local edit = request(sharp, 'textDocument/rename', private, offset(private, 'privateUse') + 1, { newName = '#new' })
      assert(present(edit) and not present(edit.changes) and #edit.documentChanges == 1, vim.inspect(edit))
      assert(type(edit.documentChanges[1].textDocument.version) == 'number')
      assert(#edit.documentChanges[1].edits == 2)
      vim.lsp.util.apply_workspace_edit(edit, sharp.offset_encoding)
      assert(text(private):find('/*privateUse*/#new', 1, true))
      assert(text(private):find('class Other { #old', 1, true))
      local rebound = request(sharp, 'textDocument/prepareRename', private, offset(private, 'privateUse') + 1)
      assert(present(rebound) and rebound.placeholder == '#new')
      result.versionedPrivateRename = { applied = true, version = edit.documentChanges[1].textDocument.version }
      result.observations.appliedPrivateEdit = edit
    end)
  else
    local ts = start('typescript', main)
    scenario('two-real-servers-have-one-ordinary-navigation-owner', function()
      assert(not sharp:supports_method('textDocument/definition', main))
      assert(not sharp:supports_method('textDocument/references', main))
      assert(not sharp:supports_method('textDocument/rename', main))
      assert(ts:supports_method('textDocument/definition', main))
      assert(not present(request(sharp, 'textDocument/hover', main, offset(main, 'member') + 1)))
      hover(ts, main, 'member', 'number')
      expect_definition(ts, main, 'member', config.files['dep.ts'].source)
      local all
      vim.lsp.buf_request_all(main, 'textDocument/definition', params(main, offset(main, 'member') + 1),
        function(values) all = values end)
      wait_until(function() return all ~= nil end, 'Native multi-client definition did not finish')
      assert(vim.tbl_count(all) == 1 and all[ts.id], vim.inspect(all))
      result.ordinaryNavigationOwner = ts.name
      result.nativeOrdinaryHover = native_hover(main, offset(main, 'member') + 1, 'number')
    end)
    scenario('ordinary-diagnostics-have-only-the-typescript-owner', function()
      local sharp_before, ts_before = diagnostic_count(sharp, main), diagnostic_count(ts, main)
      set_source(main, config.files['main.ts'].source .. 'const mismatch: number = "wrong";\n')
      hover(ts, main, 'member', 'number')
      request(sharp, 'textDocument/hover', main, offset(main, 'member') + 1)
      wait_diagnostics(ts, main, ts_before, function(values)
        for _, value in ipairs(values) do if value.message:find('assignable', 1, true) then return true end end
        return false
      end)
      wait_diagnostics(sharp, main, sharp_before, function(values) return #values == 0 end)
      result.ordinaryDiagnosticsOwner = ts.name
      set_source(main, config.files['main.ts'].source)
    end)
    scenario('unique-clr-response-reaches-native-merged-hover', function()
      local clr = open('clr.ts'); attach(sharp, clr); attach(ts, clr)
      hover(sharp, clr, 'clr', 'System.Text.StringBuilder')
      local ordinary = request(ts, 'textDocument/hover', clr, offset(clr, 'clr') + 1)
      assert(not present(ordinary) or not vim.json.encode(ordinary):find('System.Text.StringBuilder', 1, true))
      result.nativeClrHover = native_hover(clr, offset(clr, 'clr') + 1, 'System.Text.StringBuilder')
      save_and_verify(sharp, clr, 'clr.ts', 'clr', 'System.Text.StringBuilder')
      result.clrHoverOwner = sharp.name
    end)
    scenario('coexistence-ts-tsx-formatting-stays-editor-owned', function()
      save_and_verify(ts, main, 'main.ts', 'member', 'number')
      local view = open('view.tsx'); attach(sharp, view); attach(ts, view)
      save_and_verify(ts, view, 'view.tsx', 'tsxUse', 'number')
      assert(not sharp:supports_method('textDocument/formatting', view))
      local hooks = {}
      for _, autocmd in ipairs(vim.api.nvim_get_autocmds({ group = 'sharpts_prettier', event = 'BufWritePre' })) do
        hooks[autocmd.id] = true
      end
      assert(vim.tbl_count(hooks) == 1, 'Exactly one save hook with .ts/.tsx patterns required')
      result.formattingOwner = 'one external Prettier BufWritePre hook; no LSP formatting call'
    end)
    assert(result.typescriptBackend and result.typescriptBackend.version == config.typescript,
      'Actual tsserver must report pinned TypeScript: ' .. vim.inspect(result.typescriptBackend))
  end
  for name, file in pairs(config.files) do
    local buf = vim.fn.bufnr(file.path)
    if buf >= 0 and vim.api.nvim_buf_is_loaded(buf) then result.sources[name] = text(buf) end
  end
  result.completed = true
end

local ok, error = xpcall(run, debug.traceback)
if not ok then result.completed = false; result.error = phase .. ': ' .. error end
result.lastPhase = phase
result.diagnostics = diagnostics
result.diagnosticCounts = diagnostic_counts
for id in pairs(clients) do local client = vim.lsp.get_client_by_id(id); if client then pcall(function() client:stop() end) end end
vim.wait(5000, function()
  for id in pairs(clients) do local client = vim.lsp.get_client_by_id(id); if client and not client:is_stopped() then return false end end
  return true
end, 20)
for id in pairs(clients) do local client = vim.lsp.get_client_by_id(id); if client then pcall(function() client:stop(true) end) end end
vim.fn.writefile({ vim.json.encode(result) }, config.result)
if not ok then io.stderr:write(result.error .. '\n'); vim.cmd.cquit() end
