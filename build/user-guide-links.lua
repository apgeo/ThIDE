-- Pandoc Lua filter for the merged User Guide (PDF / HTML).
--
-- When the separate Markdown pages are concatenated into one document, their relative
-- cross-file links (e.g. "01-introduction.md", "../features.md") would otherwise stay as
-- file references — which a PDF viewer resolves against the PDF's own folder, giving broken
-- absolute paths like  ...\ThIDE\Assets\README.md . This filter rewrites them so every link
-- works in the single output document:
--
--   * A link to another User Guide page (a sibling ".md", i.e. no "/" in the path) becomes an
--     internal anchor "#<basename>" that jumps within the document. Each page's single level-1
--     heading is given the id "<basename>" so those anchors resolve.
--   * A link to any other repo file ("../features.md", "../../README.md", a source file, …)
--     becomes an absolute GitHub URL so it still opens (in a browser).
--   * Absolute URLs (http/https/mailto) and pure "#fragment" links are left untouched.
--
-- The ordered list of page basenames is passed in via the GUIDE_PAGES env var by the build
-- script (build-user-guide.ps1 / .sh), in the same order the pages are fed to pandoc — this
-- relies on each page having exactly one level-1 heading, at its top.

local repo = "https://github.com/apgeo/ThIDE/blob/main/"
local base = "docs/user-guide/"   -- where the merged pages live, for resolving "../" paths

-- Ordered page basenames + a membership set, from GUIDE_PAGES ("README,01-introduction,...").
local pages, pageset = {}, {}
for name in (os.getenv("GUIDE_PAGES") or ""):gmatch("([^,]+)") do
  name = name:gsub("%s+", "")
  if #name > 0 then pages[#pages + 1] = name; pageset[name] = true end
end

-- Give the k-th level-1 heading the id of the k-th page, so "#<basename>" anchors resolve.
local h1 = 0
function Header(el)
  if el.level == 1 then
    h1 = h1 + 1
    if pages[h1] then el.identifier = pages[h1]; return el end
  end
end

-- Resolve "a/b/../c"-style paths (collapse ".." and ".").
local function normalize(path)
  local parts = {}
  for seg in path:gmatch("([^/]+)") do
    if seg == ".." then
      if #parts > 0 and parts[#parts] ~= ".." then table.remove(parts) else parts[#parts + 1] = seg end
    elseif seg ~= "." and seg ~= "" then
      parts[#parts + 1] = seg
    end
  end
  return table.concat(parts, "/")
end

function Link(el)
  local target = el.target
  -- Leave absolute URLs (scheme:), mailto:, and in-document #anchors alone.
  if target:match("^%a[%w+.%-]*:") or target:sub(1, 1) == "#" then return nil end

  local path, frag = target:match("^([^#]*)(#?.*)$")
  if path == nil or path == "" then return nil end

  -- Sibling User Guide page → internal anchor (jump to that page's top).
  if not path:find("/") then
    local bn = path:gsub("%.md$", "")
    if pageset[bn] then el.target = "#" .. bn; return el end
  end

  -- Any other repo file → absolute GitHub URL (keeping a #fragment for .md heading anchors).
  el.target = repo .. normalize(base .. path) .. frag
  return el
end
