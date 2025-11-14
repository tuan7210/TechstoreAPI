CREATE DATABASE tech_store;
USE tech_store;
CREATE TABLE User (
    user_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    email VARCHAR(255) UNIQUE,
    password_hash VARCHAR(255),
    role ENUM('customer','admin'),
    phone VARCHAR(20),
    address TEXT,
    status VARCHAR(20) DEFAULT 'active', -- Trạng thái tài khoản: active, blocked
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

CREATE TABLE Category (
    category_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255),
    description TEXT
);

CREATE TABLE Product (
    product_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(255) NOT NULL, -- Thêm NOT NULL để đảm bảo tên sản phẩm luôn có
    description TEXT,
    price DECIMAL(10,2) NOT NULL, -- Thêm NOT NULL
    original_price DECIMAL(10,2),
    brand VARCHAR(255),
    stock_quantity INT DEFAULT 0, -- Set DEFAULT
    category_id INT NOT NULL, -- Thêm NOT NULL
    image_url VARCHAR(255),
    specifications JSON,
    rating DECIMAL(2,1) DEFAULT 0.0,
    review_count INT DEFAULT 0,
    is_new BOOLEAN DEFAULT FALSE,
    is_best_seller BOOLEAN DEFAULT FALSE,
    
    -- CỘT BỔ SUNG CHO CHATBOT AI
    use_case TEXT,             -- Mô tả ngữ cảnh sử dụng (Cho ai? Làm gì?)
    usp TEXT,                  -- Điểm bán hàng độc đáo (Unique Selling Points)
    warranty_period VARCHAR(50), -- Thời gian bảo hành (Ví dụ: '12 tháng')
    return_policy_days INT,    -- Số ngày đổi trả (Ví dụ: 7)
    
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (category_id) REFERENCES Category(category_id)
);

CREATE TABLE Cart (
    cart_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    FOREIGN KEY (user_id) REFERENCES User(user_id)
);

CREATE TABLE CartItem (
    cart_item_id INT AUTO_INCREMENT PRIMARY KEY,
    cart_id INT,
    product_id INT,
    quantity INT,
    FOREIGN KEY (cart_id) REFERENCES Cart(cart_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id)
);

CREATE TABLE OrderTable (
    order_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    order_date TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    status ENUM('pending', 'shipped', 'completed', 'canceled'),
    total_amount DECIMAL(10,2),
    shipping_address TEXT,
    payment_status ENUM('unpaid', 'paid'),
    payment_method VARCHAR(50),
    FOREIGN KEY (user_id) REFERENCES User(user_id)
);

CREATE TABLE OrderItem (
    order_item_id INT AUTO_INCREMENT PRIMARY KEY,
    order_id INT,
    product_id INT,
    quantity INT,
    price DECIMAL(10,2),
    FOREIGN KEY (order_id) REFERENCES OrderTable(order_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id)
);

CREATE TABLE Review (
    review_id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT,
    product_id INT,
    order_item_id INT,
    rating INT CHECK (rating BETWEEN 1 AND 5),
    comment TEXT,
    is_verified BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES User(user_id),
    FOREIGN KEY (product_id) REFERENCES Product(product_id),
    FOREIGN KEY (order_item_id) REFERENCES OrderItem(order_item_id),
    UNIQUE KEY unique_review_per_order_item (order_item_id),
    INDEX idx_product_rating (product_id, rating),
    INDEX idx_user_reviews (user_id, created_at)
);

-- Trigger để tự động verify review khi order đã completed
DELIMITER //
CREATE TRIGGER verify_review_after_insert
    AFTER INSERT ON Review
    FOR EACH ROW
BEGIN
    DECLARE order_status VARCHAR(20);
    
    SELECT ot.status INTO order_status
    FROM OrderTable ot
    JOIN OrderItem oi ON ot.order_id = oi.order_id
    WHERE oi.order_item_id = NEW.order_item_id;
    
    IF order_status = 'completed' THEN
        UPDATE Review 
        SET is_verified = TRUE 
        WHERE review_id = NEW.review_id;
    END IF;
END//
DELIMITER ;

-- Trigger để verify review khi order status thay đổi thành completed
DELIMITER //
CREATE TRIGGER verify_reviews_on_order_complete
    AFTER UPDATE ON OrderTable
    FOR EACH ROW
BEGIN
    IF NEW.status = 'completed' AND OLD.status != 'completed' THEN
        UPDATE Review r
        JOIN OrderItem oi ON r.order_item_id = oi.order_item_id
        SET r.is_verified = TRUE
        WHERE oi.order_id = NEW.order_id;
    END IF;
END//
DELIMITER ;

-- Trigger để cập nhật rating và review_count khi thêm review mới
DELIMITER //
CREATE TRIGGER update_product_rating_after_insert
    AFTER INSERT ON Review
    FOR EACH ROW
BEGIN
    UPDATE Product 
    SET 
        rating = (
            SELECT ROUND(AVG(rating), 1) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        ),
        review_count = (
            SELECT COUNT(*) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        )
    WHERE product_id = NEW.product_id;
END//
DELIMITER ;

-- Trigger để cập nhật rating và review_count khi cập nhật review
DELIMITER //
CREATE TRIGGER update_product_rating_after_update
    AFTER UPDATE ON Review
    FOR EACH ROW
BEGIN
    -- Cập nhật cho product của review cũ (nếu product_id thay đổi)
    IF OLD.product_id != NEW.product_id THEN
        UPDATE Product 
        SET 
            rating = (
                SELECT COALESCE(ROUND(AVG(rating), 1), 0) 
                FROM Review 
                WHERE product_id = OLD.product_id AND is_verified = TRUE
            ),
            review_count = (
                SELECT COUNT(*) 
                FROM Review 
                WHERE product_id = OLD.product_id AND is_verified = TRUE
            )
        WHERE product_id = OLD.product_id;
    END IF;
    
    -- Cập nhật cho product của review mới
    UPDATE Product 
    SET 
        rating = (
            SELECT COALESCE(ROUND(AVG(rating), 1), 0) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        ),
        review_count = (
            SELECT COUNT(*) 
            FROM Review 
            WHERE product_id = NEW.product_id AND is_verified = TRUE
        )
    WHERE product_id = NEW.product_id;
END//
DELIMITER ;

-- Trigger để cập nhật rating và review_count khi xóa review
DELIMITER //
CREATE TRIGGER update_product_rating_after_delete
    AFTER DELETE ON Review
    FOR EACH ROW
BEGIN
    UPDATE Product 
    SET 
        rating = (
            SELECT COALESCE(ROUND(AVG(rating), 1), 0) 
            FROM Review 
            WHERE product_id = OLD.product_id AND is_verified = TRUE
        ),
        review_count = (
            SELECT COUNT(*) 
            FROM Review 
            WHERE product_id = OLD.product_id AND is_verified = TRUE
        )
    WHERE product_id = OLD.product_id;
END//
DELIMITER ;
-- Thêm tài khoản admin mặc định

INSERT INTO User (name, email, password_hash, role, phone, address, status)
VALUES ('Administrator', 'admin@example.com', '$2a$11$LW/kugJ.FEoHUCLP0KASp.umaQEFQYJjTxQFP5XNx//3eUVmStvXi', 'admin', '0123456789', 'Hà Nội, Việt Nam', 'active'),
       ('Nguyen Van B', 'customer@example.com', '$2a$11$LW/kugJ.FEoHUCLP0KASp.umaQEFQYJjTxQFP5XNx//3eUVmStvXi', 'customer', '0987654321', 'TP.HCM, Việt Nam', 'active');

INSERT INTO Category (name, description) VALUES
('Điện thoại', 'Các loại điện thoại thông minh, điện thoại phổ thông'),
('Laptop', 'Các loại máy tính xách tay, laptop gaming, laptop văn phòng'),
('Phụ kiện', 'Tai nghe, sạc, cáp, ốp lưng, chuột máy tính'),
('Máy tính bảng','Các loại máy tính bảng chính hãng, hiệu năng cao');
INSERT INTO Product (name, description, price, original_price, brand, stock_quantity, category_id, image_url, specifications, rating, review_count, is_new, is_best_seller) VALUES
-- Điện thoại
('iPhone 15 128GB', 'iPhone 15 chính hãng VN/A, màu đen', 25000000, 27000000, 'Apple', 50, 1, 'https://example.com/images/iphone15.jpg', 
 '{"screen": "6.1 inch Super Retina XDR", "chip": "A17 Bionic", "ram": "6GB", "storage": "128GB", "camera": "48MP + 12MP", "battery": "3349mAh"}', 
 4.5, 128, TRUE, TRUE),

('Samsung Galaxy S24', 'Samsung Galaxy S24 chính hãng, màu trắng', 22000000, 24000000, 'Samsung', 40, 1, 'https://example.com/images/galaxy_s24.jpg',
 '{"screen": "6.2 inch Dynamic AMOLED 2X", "chip": "Exynos 2400", "ram": "8GB", "storage": "256GB", "camera": "50MP + 12MP + 10MP", "battery": "4000mAh"}',
 4.3, 95, TRUE, FALSE),

('Xiaomi Redmi Note 13', 'Redmi Note 13 5G chính hãng, màu xanh', 7000000, 8000000, 'Xiaomi', 60, 1, 'https://example.com/images/redmi_note13.jpg',
 '{"screen": "6.67 inch AMOLED", "chip": "Snapdragon 7s Gen 2", "ram": "8GB", "storage": "128GB", "camera": "108MP + 8MP + 2MP", "battery": "5000mAh"}',
 4.2, 67, FALSE, TRUE),

-- Laptop
('MacBook Air M3 2025', 'MacBook Air M3 8GB/256GB, màu bạc', 32000000, 35000000, 'Apple', 30, 2, 'https://example.com/images/macbook_air_m3.jpg',
 '{"screen": "13.6 inch Liquid Retina", "chip": "Apple M3", "ram": "8GB", "storage": "256GB SSD", "graphics": "8-core GPU", "weight": "1.24kg"}',
 4.6, 89, TRUE, TRUE),

('Dell XPS 13', 'Dell XPS 13 2025, i7, 16GB RAM, 512GB SSD', 35000000, 38000000, 'Dell', 25, 2, 'https://example.com/images/dell_xps_13.jpg',
 '{"screen": "13.4 inch InfinityEdge", "cpu": "Intel Core i7-1365U", "ram": "16GB LPDDR5", "storage": "512GB SSD", "graphics": "Intel Iris Xe", "weight": "1.19kg"}',
 4.4, 56, FALSE, FALSE),

('ASUS ROG Zephyrus G14', 'Laptop gaming ASUS ROG G14, Ryzen 9, 32GB RAM', 42000000, 45000000, 'ASUS', 20, 2, 'https://example.com/images/asus_rog_g14.jpg',
 '{"screen": "14 inch QHD 165Hz", "cpu": "AMD Ryzen 9 7940HS", "ram": "32GB DDR5", "storage": "1TB SSD", "graphics": "RTX 4070", "weight": "1.65kg"}',
 4.7, 43, FALSE, TRUE),

-- Phụ kiện
('Tai nghe AirPods Pro 2', 'Tai nghe AirPods Pro 2 chính hãng Apple', 5200000, 5500000, 'Apple', 80, 3, 'https://example.com/images/airpods_pro_2.jpg',
 '{"type": "In-ear", "connection": "Bluetooth 5.3", "anc": "Active Noise Cancellation", "battery": "6h + 24h with case", "features": "Spatial Audio, Transparency Mode"}',
 4.5, 234, FALSE, TRUE),

('Sạc nhanh Anker 65W', 'Sạc nhanh Anker 65W PowerPort, sạc laptop & điện thoại', 850000, 950000, 'Anker', 100, 3, 'https://example.com/images/anker_65w.jpg',
 '{"power": "65W", "ports": "USB-C x2, USB-A x1", "tech": "PowerIQ 3.0", "size": "70 x 70 x 32mm", "weight": "130g"}',
 4.6, 187, FALSE, FALSE),

('Chuột Logitech MX Master 3', 'Chuột không dây Logitech MX Master 3', 2500000, 2800000, 'Logitech', 50, 3, 'https://example.com/images/logitech_mx_master_3.jpg',
 '{"type": "Wireless", "connection": "Bluetooth + USB Receiver", "dpi": "4000 DPI", "battery": "70 days", "features": "MagSpeed scroll, Multi-device"}',
 4.4, 156, FALSE, FALSE);

