CREATE TABLE IF NOT EXISTS paths_to_scan (
    id SERIAL PRIMARY KEY,
    path TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS nodes (
    id SERIAL PRIMARY KEY,
    path_id INTEGER NOT NULL REFERENCES paths_to_scan(id),
    relative_path TEXT NOT NULL,
    type TEXT NOT NULL, -- 'file' or 'directory'
    hash TEXT,
    hash_algorithm TEXT,
    last_checked TIMESTAMP,
    UNIQUE(path_id, relative_path)
);