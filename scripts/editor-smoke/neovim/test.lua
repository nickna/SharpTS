local config = vim.json.decode(table.concat(vim.fn.readfile(assert(vim.env.SHARPTS_EDITOR_SMOKE_CONFIG)), "\n"))
local clients = {}
local result = { mode = config.mode, editor = "Neovim " .. tostring(vim.version()), fixtures = {} }
result.log = vim.lsp.log.get_filename()
vim.fn.writefile({}, result.log)
vim.lsp.log.set_level("debug")

local function run()
  dofile(config.recipe)
  for _, fixture in ipairs(config.fixtures) do
    vim.cmd.edit(vim.fn.fnameescape(fixture.file))
    local bufnr = vim.api.nvim_get_current_buf()
    vim.bo[bufnr].filetype = fixture.file:match("%.tsx$") and "typescriptreact" or "typescript"
    local id = assert(vim.lsp.start({
      name = "sharpts-editor-smoke",
      cmd = { "dotnet", config.server, "--language-features", config.mode, "--diagnostics", "sharpts-only" },
      root_dir = config.workspace,
    }))
    clients[id] = true
    local client = assert(vim.lsp.get_client_by_id(id))
    assert(vim.wait(10000, function() return client.initialized end, 20), "LSP initialize timed out")
    local caps = client.server_capabilities
    assert(not caps.documentFormattingProvider, "SharpTS advertised document formatting")
    assert(not caps.documentRangeFormattingProvider, "SharpTS advertised range formatting")
    assert(not caps.documentOnTypeFormattingProvider, "SharpTS advertised on-type formatting")
    if config.mode == "full" then
      assert(vim.wait(5000, function()
        return client:supports_method("textDocument/definition", bufnr)
      end, 20), "full-mode definition registration timed out")
    else
      assert(not client:supports_method("textDocument/definition", bufnr), "interop-only registered definition")
    end
    assert(not client:supports_method("textDocument/formatting", bufnr), "SharpTS registered formatting")
    assert(not client:supports_method("textDocument/rangeFormatting", bufnr), "SharpTS registered range formatting")
    assert(not client:supports_method("textDocument/onTypeFormatting", bufnr), "SharpTS registered on-type formatting")
    local lines = vim.split(fixture.edited:gsub("\n$", ""), "\n", { plain = true })
    vim.api.nvim_buf_set_lines(bufnr, 0, -1, false, lines)
    assert(vim.bo[bufnr].modified, "fixture must have an unsaved edit")
    local function hover()
      local response = assert(client:request_sync("textDocument/hover", {
        textDocument = { uri = vim.uri_from_fname(fixture.file) },
        position = { line = 0, character = 5 },
      }, 5000, bufnr))
      assert(not response.err, vim.inspect(response.err))
      local text = vim.json.encode(response.result)
      assert(text:find("System.Text.StringBuilder", 1, true), "interop hover did not resolve")
      assert(text:find("Represents a mutable string of characters", 1, true), "CLR documentation missing")
      return response.result
    end
    local function save()
      vim.cmd.write()
      local file = assert(io.open(fixture.file, "rb"))
      local actual = file:read("*a")
      file:close()
      assert(actual == fixture.expected, "format-on-save bytes differ from pinned Prettier: " .. fixture.file)
    end
    local before = hover()
    save()
    local after = hover()
    local formatted = vim.api.nvim_buf_get_lines(bufnr, 0, -1, false)
    formatted[#formatted] = formatted[#formatted] .. "  "
    vim.api.nvim_buf_set_lines(bufnr, 0, -1, false, formatted)
    assert(vim.bo[bufnr].modified, "second save must include a dirty edit")
    save()
    local afterSecond = hover()
    table.insert(result.fixtures, {
      file = fixture.file, hoverBefore = before, hoverAfter = after,
      hoverAfterSecondSave = afterSecond, formatted = true, secondSaveStable = true,
    })
  end
  result.completed = true
end

local ok, err = xpcall(run, debug.traceback)
if not ok then
  result.completed = false
  result.error = err
end
for id in pairs(clients) do
  local client = vim.lsp.get_client_by_id(id)
  if client then client:stop() end
end
vim.wait(3000, function()
  for id in pairs(clients) do
    local client = vim.lsp.get_client_by_id(id)
    if client and not client:is_stopped() then return false end
  end
  return true
end, 20)
for id in pairs(clients) do
  local client = vim.lsp.get_client_by_id(id)
  if client then client:stop(true) end
end
vim.fn.writefile({ vim.json.encode(result) }, config.result)
if not ok then
  io.stderr:write(err .. "\n")
  vim.cmd.cquit()
end
