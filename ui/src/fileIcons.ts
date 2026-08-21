export type FileBadge =
  | { kind: 'folder' }
  | { kind: 'file'; label: string; bg: string; fg: string };

const NEUTRAL: Omit<Extract<FileBadge, { kind: 'file' }>, 'kind'> = { label: '•', bg: '#64748b', fg: '#fff' };

export function fileBadge(name: string, isDirectory: boolean, extension: string): FileBadge {
  if (isDirectory) return { kind: 'folder' };
  const base = name.toLowerCase();
  const ext = extension.toLowerCase();
  const named = byFileName(base);
  if (named) return named;
  const mapped = byExtension(ext);
  if (mapped) return mapped;
  if (ext) return { kind: 'file', label: ext.slice(0, 3).toUpperCase(), bg: '#64748b', fg: '#fff' };
  return { kind: 'file', ...NEUTRAL };
}

function file(label: string, bg: string, fg: string): FileBadge {
  return { kind: 'file', label, bg, fg };
}

function byFileName(base: string): FileBadge | null {
  if (base === '.gitignore') return file('GI', '#64748b', '#fff');
  if (base === 'dockerfile' || base.startsWith('dockerfile.')) return file('DK', '#64748b', '#e2e8f0');
  if (base === '.env' || base.startsWith('.env.')) return file('ENV', '#4d7c0f', '#fff');
  if (base === 'go.mod' || base === 'go.sum') return file('GO', '#0891b2', '#fff');
  if (base === 'pom.xml' || base === 'build.gradle' || base === 'build.gradle.kts') return file('JA', '#ea580c', '#fff');
  return null;
}

function byExtension(ext: string): FileBadge | null {
  switch (ext) {
    case 'ts': case 'tsx': return file('TS', '#3b82f6', '#fff');
    case 'js': case 'jsx': case 'mjs': case 'cjs': return file('JS', '#ca8a04', '#1a1608');
    case 'py': return file('PY', '#eab308', '#1a1608');
    case 'cs': case 'csproj': case 'sln': return file('CS', '#8b5cf6', '#fff');
    case 'java': return file('JA', '#ea580c', '#fff');
    case 'go': return file('GO', '#0891b2', '#fff');
    case 'css': case 'scss': case 'less': return file('CSS', '#c084fc', '#1a1224');
    case 'html': case 'htm': return file('HTM', '#f97316', '#1a0f08');
    case 'json': return file('JSON', '#ca8a04', '#1a1608');
    case 'md': case 'markdown': return file('MD', '#64748b', '#fff');
    case 'ps1': return file('PS', '#3178c6', '#fff');
    case 'png': case 'jpg': case 'jpeg': case 'gif': case 'webp': case 'ico': case 'svg':
      return file('IMG', '#16a34a', '#fff');
    default: return null;
  }
}
