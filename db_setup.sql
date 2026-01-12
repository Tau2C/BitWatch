CREATE TABLE IF NOT EXISTS paths_to_scan (
    id SERIAL PRIMARY KEY,
    path TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS nodes (
    id SERIAL PRIMARY KEY,
    path_id INTEGER NOT NULL REFERENCES paths_to_scan(id) ON DELETE CASCADE,
    relative_path TEXT NOT NULL,
    type TEXT NOT NULL, -- 'file' or 'directory'
    hash TEXT,
    hash_algorithm TEXT,
    last_checked TIMESTAMP,
    UNIQUE(path_id, relative_path)
);

CREATE TABLE IF NOT EXISTS excluded_nodes (
    id SERIAL PRIMARY KEY,
    path_id INTEGER NOT NULL REFERENCES paths_to_scan(id) ON DELETE CASCADE,
    relative_path TEXT NOT NULL,
    UNIQUE(path_id, relative_path)
);

CREATE OR REPLACE FUNCTION add_and_clean_excluded_node(
    p_path_id INTEGER,
    p_relative_path TEXT
)
RETURNS void AS $$
BEGIN
    -- Add the entry to excluded_nodes
    INSERT INTO excluded_nodes (path_id, relative_path)
    VALUES (p_path_id, p_relative_path)
    ON CONFLICT (path_id, relative_path) DO NOTHING;

    -- Remove nodes that are in the excluded path
    DELETE FROM nodes
    WHERE path_id = p_path_id
      AND (
            relative_path = p_relative_path -- For exact file/directory match
            OR relative_path LIKE p_relative_path || '/%' -- For children of excluded directory
          );
END;
$$ LANGUAGE plpgsql;

CREATE TABLE IF NOT EXISTS app_settings (
    key TEXT PRIMARY KEY,
    value TEXT
);
